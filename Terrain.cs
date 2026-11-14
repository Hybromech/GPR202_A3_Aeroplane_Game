using System;
using System.Collections.Generic;
using System.IO;
using Assimp;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using StbImageSharp;

namespace GPR202_Assessment3
{
    public class Terrain : GameObject
    {
        public static readonly uint width = 2048;
        public static readonly uint length = 2048;
        public static float OffsetY = -600;
        private static float[,] heightmap;

        private List<float> vertexdata;
        List<uint> indices;

        private int heightmapHandle;
        private int terrainShaderProgram;

        // PERFORMANCE OPTIMIZATION: Cached uniform locations.
        // Querying uniform locations via string names every frame forces expensive driver 
        // string lookups. Caching them once during Setup() eliminates per-frame CPU overhead.
        private int modelLocation;
        private int viewLocation;
        private int projectionLocation;

        string vertexShaderCode =
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

        string fragShaderCode =
        @"
                #version 330 core

                in vec2 texCoord;
                out vec4 pixelColor;

                uniform sampler2D texture0;

                void main()
                {
                    vec4 texColor = texture(texture0, texCoord);
                    pixelColor = texColor;
                }
            ";

        public Terrain()
        {
            heightmap = new float[width, length];
            ImportHeightMap();
            Setup();
        }

        public void Draw(Matrix4 view, Matrix4 proj)
        {
            scale = 1f;
            position = new Vector3(0f, OffsetY, 0f);
            model = Matrix4.Identity * Matrix4.CreateScale(scale) * Matrix4.CreateRotationX(rotation.X) * Matrix4.CreateRotationY(rotation.Y) * Matrix4.CreateTranslation(position);

            GL.UseProgram(terrainShaderProgram);

            // PERFORMANCE OPTIMIZATION: Use cached uniform locations instead of GL.GetUniformLocation every frame.
            GL.UniformMatrix4(modelLocation, true, ref model);
            GL.UniformMatrix4(viewLocation, true, ref view);
            GL.UniformMatrix4(projectionLocation, true, ref proj);

            GL.BindVertexArray(vao);
            // Note: EBO and VBO are already bound within the VAO state during Setup(). 
            // Binding vertex array alone is sufficient in modern OpenGL core profiles.

            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, textureHandle);

            GL.DrawElements(OpenTK.Graphics.OpenGL4.PrimitiveType.Triangles, indices.Count, DrawElementsType.UnsignedInt, 0);
        }

        public static float GetHeightMapAt(int x, int z)
        {
            return heightmap[x, z];
        }

        public static Vector3 WorldPositionFromHeightMap(int x, int z)
        {
            return new Vector3(x - ((int)width / 2), OffsetY + GetHeightMapAt(x, z), z - ((int)length / 2));
        }

        private void ImportHeightMap()
        {
            heightmapHandle = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, heightmapHandle);

            string hm_fullpath = "Resource/Terrain/heightmap.png";
            if (!File.Exists(hm_fullpath))
            {
                throw new FileNotFoundException("Texture not found at '" + hm_fullpath + "'");
            }

            vertexdata = new List<float>();
            using (var stream = File.OpenRead(hm_fullpath))
            {
                ImageResult image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);

                for (uint x = 0; x < width; x++)
                {
                    for (uint z = 0; z < length; z++)
                    {
                        byte texel = image.Data[(x + width * z) * 4];
                        float r = image.Data[(x + width * z) * 4] + 0;
                        float g = image.Data[(x + width * z) * 4] + 1;
                        float b = image.Data[(x + width * z) * 4] + 2;
                        float h = ((r + g + b) / 3) * 4f;
                        heightmap[x, z] = h;
                    }
                }

                for (uint x = 0; x < width; x++)
                {
                    for (uint z = 0; z < length; z++)
                    {
                        float h = heightmap[x, z];

                        int smooth = 4;
                        if (x > smooth && x < width - 1 - smooth && z > smooth && z < length - 1 - smooth)
                        {
                            float sum = 0;
                            float e = 0;
                            for (int i = -smooth; i < smooth; i++)
                            {
                                for (int j = -smooth; j < smooth; j++)
                                {
                                    sum += heightmap[x + i, z + j];
                                    e++;
                                }
                            }
                            h = sum / e;
                        }

                        float TILE = 1.0f;
                        float xpos = (-width / 2.0f) + x;
                        float zpos = (-length / 2.0f) + z;

                        vertexdata.Add(xpos * TILE);
                        vertexdata.Add(h);
                        vertexdata.Add(zpos * TILE);

                        vertexdata.Add((1.0f / 2048f) * x);
                        vertexdata.Add((1.0f / 2048f) * z);
                    }
                }

                indices = new List<uint>();
                for (uint i = 0; i < (width - 1); i++)
                {
                    for (uint j = 0; j < (length - 1); j++)
                    {
                        indices.Add((j * width) + i);
                        indices.Add((j * width) + i + 1);
                        indices.Add(((j + 1) * width) + i + 1);

                        indices.Add(((j + 1) * width) + i + 1);
                        indices.Add(((j + 1) * width) + i);
                        indices.Add((j * width) + i);
                    }
                }
            }

            GL.BindTexture(TextureTarget.Texture2D, 0);
            GL.DeleteTexture(heightmapHandle);

            textureHandle = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, textureHandle);

            StbImage.stbi_set_flip_vertically_on_load(1);
            string fullpath = "Resource/Terrain/diffuse.png";
            if (!File.Exists(fullpath))
            {
                throw new FileNotFoundException("Texture not found at '" + fullpath + "'");
            }
            using (var stream = File.OpenRead(fullpath))
            {
                ImageResult image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, image.Width, image.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, image.Data);
            }

            GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)OpenTK.Graphics.OpenGL4.TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)OpenTK.Graphics.OpenGL4.TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapR, (int)OpenTK.Graphics.OpenGL4.TextureWrapMode.Repeat);
        }

        private void Setup()
        {
            vbo = GL.GenBuffer();
            vao = GL.GenVertexArray();
            GL.BindVertexArray(vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);

            float[] _vbo = vertexdata.ToArray();

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

            terrainShaderProgram = GL.CreateProgram();
            GL.AttachShader(terrainShaderProgram, vertexShaderObject);
            GL.AttachShader(terrainShaderProgram, fragmentShaderObject);
            GL.LinkProgram(terrainShaderProgram);

            GL.DetachShader(terrainShaderProgram, vertexShaderObject);
            GL.DetachShader(terrainShaderProgram, fragmentShaderObject);
            GL.DeleteShader(vertexShaderObject);
            GL.DeleteShader(fragmentShaderObject);

            // PERFORMANCE OPTIMIZATION: Cache uniform locations once after linking.
            modelLocation = GL.GetUniformLocation(terrainShaderProgram, "model");
            viewLocation = GL.GetUniformLocation(terrainShaderProgram, "view");
            projectionLocation = GL.GetUniformLocation(terrainShaderProgram, "projection");

            GL.UseProgram(terrainShaderProgram);
        }
    }
}