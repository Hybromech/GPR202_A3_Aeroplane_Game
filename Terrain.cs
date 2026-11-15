using System;
using System.Collections.Generic;
using System.IO;
using Assimp;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using StbImageSharp;

namespace GPR202_Assessment3
{
    public class Terrain
    {
        public static readonly uint width = 2048;
        public static readonly uint length = 2048;
        public static float OffsetY = -600;
        private static float[,] heightmap;

        private List<TerrainChunk> chunks;
        private int textureHandle;
        private int terrainShaderProgram;

        // Cached uniform locations
        private int modelLocation;
        private int viewLocation;
        private int projectionLocation;

        private const uint CHUNK_SIZE = 128;

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
            SetupShaders();
            CreateChunks();
        }

        public void Draw(Matrix4 view, Matrix4 proj)
        {
            GL.UseProgram(terrainShaderProgram);

            // PERFORMANCE OPTIMIZATION: Upload view and projection matrices ONCE per frame 
            // instead of repeatedly inside every individual chunk draw call.
            GL.UniformMatrix4(viewLocation, true, ref view);
            GL.UniformMatrix4(projectionLocation, true, ref proj);

            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, textureHandle);

            // Draw each terrain chunk
            for (int i = 0; i < chunks.Count; i++)
            {
                chunks[i].Draw(terrainShaderProgram, modelLocation);
            }
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
            int heightmapHandle = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, heightmapHandle);

            string hm_fullpath = "Resource/Terrain/heightmap.png";
            if (!File.Exists(hm_fullpath))
            {
                throw new FileNotFoundException("Texture not found at '" + hm_fullpath + "'");
            }

            float[,] rawHeightmap = new float[width, length];

            using (var stream = File.OpenRead(hm_fullpath))
            {
                ImageResult image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);

                for (uint x = 0; x < width; x++)
                {
                    for (uint z = 0; z < length; z++)
                    {
                        float r = image.Data[(x + width * z) * 4];
                        float g = image.Data[(x + width * z) * 4] + 1;
                        float b = image.Data[(x + width * z) * 4] + 2;
                        float h = ((r + g + b) / 3) * 4f;
                        rawHeightmap[x, z] = h;
                    }
                }

                // Apply smoothing and store in global heightmap
                int smooth = 4;
                for (uint x = 0; x < width; x++)
                {
                    for (uint z = 0; z < length; z++)
                    {
                        float h = rawHeightmap[x, z];
                        if (x > smooth && x < width - 1 - smooth && z > smooth && z < length - 1 - smooth)
                        {
                            float sum = 0;
                            float e = 0;
                            for (int i = -smooth; i < smooth; i++)
                            {
                                for (int j = -smooth; j < smooth; j++)
                                {
                                    sum += rawHeightmap[x + i, z + j];
                                    e++;
                                }
                            }
                            h = sum / e;
                        }
                        heightmap[x, z] = h;
                    }
                }
            }

            GL.DeleteTexture(heightmapHandle);

            // Load Terrain Texture
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

        private void SetupShaders()
        {
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

            modelLocation = GL.GetUniformLocation(terrainShaderProgram, "model");
            viewLocation = GL.GetUniformLocation(terrainShaderProgram, "view");
            projectionLocation = GL.GetUniformLocation(terrainShaderProgram, "projection");
        }

        private void CreateChunks()
        {
            chunks = new List<TerrainChunk>();

            for (uint x = 0; x < width; x += CHUNK_SIZE)
            {
                for (uint z = 0; z < length; z += CHUNK_SIZE)
                {
                    chunks.Add(new TerrainChunk(x, z, CHUNK_SIZE, heightmap));
                }
            }
        }
    }

    public class TerrainChunk
    {
        private int vao, vbo, ebo;
        private int indexCount;
        private Matrix4 model;

        public TerrainChunk(uint startX, uint startZ, uint size, float[,] hm)
        {
            List<float> vertexdata = new List<float>();
            List<uint> indices = new List<uint>();

            uint endX = Math.Min(startX + size, Terrain.width - 1);
            uint endZ = Math.Min(startZ + size, Terrain.length - 1);

            uint spanX = (endX - startX) + 1;
            uint spanZ = (endZ - startZ) + 1;

            // PERFORMANCE OPTIMIZATION: Pre-allocate vertex and index capacities 
            // to eliminate internal array resizing allocations during generation.
            vertexdata.Capacity = (int)(spanX * spanZ * 5);
            indices.Capacity = (int)((spanX - 1) * (spanZ - 1) * 6);

            for (uint x = startX; x <= endX; x++)
            {
                for (uint z = startZ; z <= endZ; z++)
                {
                    float h = hm[x, z];
                    float TILE = 1.0f;
                    float xpos = (-Terrain.width / 2.0f) + x;
                    float zpos = (-Terrain.length / 2.0f) + z;

                    vertexdata.Add(xpos * TILE);
                    vertexdata.Add(h);
                    vertexdata.Add(zpos * TILE);

                    vertexdata.Add((1.0f / 2048f) * x);
                    vertexdata.Add((1.0f / 2048f) * z);
                }
            }

            // PERFORMANCE OPTIMIZATION: Replaced slow Dictionary lookup with direct 
            // mathematical 2D-to-1D grid index calculation.
            for (uint x = 0; x < spanX - 1; x++)
            {
                for (uint z = 0; z < spanZ - 1; z++)
                {
                    uint i0 = x * spanZ + z;
                    uint i1 = (x + 1) * spanZ + z;
                    uint i2 = (x + 1) * spanZ + (z + 1);
                    uint i3 = x * spanZ + (z + 1);

                    indices.Add(i0);
                    indices.Add(i1);
                    indices.Add(i2);

                    indices.Add(i2);
                    indices.Add(i3);
                    indices.Add(i0);
                }
            }

            indexCount = indices.Count;
            Vector3 position = new Vector3(0f, Terrain.OffsetY, 0f);
            model = Matrix4.Identity * Matrix4.CreateTranslation(position);

            SetupBuffers(vertexdata, indices);
        }

        private void SetupBuffers(List<float> vertexdata, List<uint> indices)
        {
            vao = GL.GenVertexArray();
            vbo = GL.GenBuffer();
            ebo = GL.GenBuffer();

            GL.BindVertexArray(vao);

            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            float[] _vbo = vertexdata.ToArray();
            GL.BufferData(BufferTarget.ArrayBuffer, sizeof(float) * _vbo.Length, _vbo, BufferUsageHint.StaticDraw);

            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 5 * sizeof(float), 0);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 5 * sizeof(float), 3 * sizeof(float));
            GL.EnableVertexAttribArray(1);

            GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
            uint[] _ebo = indices.ToArray();
            GL.BufferData(BufferTarget.ElementArrayBuffer, _ebo.Length * sizeof(uint), _ebo, BufferUsageHint.StaticDraw);

            GL.BindVertexArray(0);
        }

        public void Draw(int shaderProgram, int modelLoc)
        {
            // PERFORMANCE OPTIMIZATION: Only upload the model matrix per chunk; 
            // view and projection matrices are set once per frame in Terrain.Draw().
            GL.UniformMatrix4(modelLoc, true, ref model);

            GL.BindVertexArray(vao);
            GL.DrawElements(OpenTK.Graphics.OpenGL4.PrimitiveType.Triangles, indexCount, DrawElementsType.UnsignedInt, 0);
            GL.BindVertexArray(0);
        }
    }
}