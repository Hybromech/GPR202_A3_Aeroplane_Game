using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
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
        private Frustum frustum;

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
                layout (location = 2) in vec3 aNormal;

                uniform mat4 model;
                uniform mat4 view;
                uniform mat4 projection;

                out vec2 texCoord;
                out vec3 fragPos;
                out vec3 normal;

                void main()
                {
                    texCoord = aTexCoord;
                    vec4 worldPosition = vec4(aPosition, 1.0f) * model;
                    fragPos = worldPosition.xyz;
                    normal = normalize(aNormal * mat3(model));
                    gl_Position = worldPosition * view * projection;
                }";

        string fragShaderCode =
            @"
                #version 330 core

                in vec2 texCoord;
                in vec3 fragPos;
                in vec3 normal;

                out vec4 pixelColor;

                struct DirLight {
                    vec3 direction;
                    vec3 ambient;
                    vec3 diffuse;
                    vec3 specular;
                };

                uniform sampler2D texture0;
                uniform DirLight dirLight;
                uniform vec3 viewPos;

                void main()
                {
                    vec3 baseColor = texture(texture0, texCoord).rgb;
                    vec3 norm = normalize(normal);
                    vec3 lightDir = normalize(-dirLight.direction);
                    vec3 viewDir = normalize(viewPos - fragPos);

                    float diff = max(dot(norm, lightDir), 0.0);

                    vec3 halfwayDir = normalize(lightDir + viewDir);
                    float spec = pow(max(dot(norm, halfwayDir), 0.0), 32.0);

                    vec3 ambient  = dirLight.ambient * baseColor;
                    vec3 diffuse  = dirLight.diffuse * diff * baseColor;
                    vec3 specular = dirLight.specular * spec * baseColor;

                    pixelColor = vec4(ambient + diffuse + specular, 1.0);
                }
            ";

        public Terrain()
        {
            heightmap = new float[width, length];
            frustum = new Frustum();
            ImportHeightMap();
            SetupShaders();
            CreateChunks();
        }

        public void Draw(Matrix4 view, Matrix4 proj, DirectionalLight sunLight, Vector3 viewPos)
        {
            GL.UseProgram(terrainShaderProgram);

            sunLight?.ApplyToShader(terrainShaderProgram, viewPos);

            // Upload view and projection matrices ONCE per frame
            GL.UniformMatrix4(viewLocation, true, ref view);
            GL.UniformMatrix4(projectionLocation, true, ref proj);

            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, textureHandle);

            // Update camera frustum planes from standard View-Projection matrix
            Matrix4 viewProj = view * proj;
            frustum.Update(viewProj);

            // Draw only terrain chunks whose bounding spheres are visible within the camera frustum
            for (int i = 0; i < chunks.Count; i++)
            {
                if (chunks[i].IsInFrustum(frustum))
                {
                    chunks[i].Draw(modelLocation);
                }
            }
        }

        public static float GetHeightMapAt(int x, int z)
        {
            return heightmap[x, z];
        }

        public static Vector3 GetNormalAt(int x, int z)
        {
            int xL = Math.Max(x - 1, 0);
            int xR = Math.Min(x + 1, (int)width - 1);
            int zD = Math.Max(z - 1, 0);
            int zU = Math.Min(z + 1, (int)length - 1);

            float hL = heightmap[xL, z];
            float hR = heightmap[xR, z];
            float hD = heightmap[x, zD];
            float hU = heightmap[x, zU];

            float dx = (xR - xL) > 0 ? (xR - xL) : 1f;
            float dz = (zU - zD) > 0 ? (zU - zD) : 1f;

            Vector3 n = new Vector3((hL - hR) / dx, 2.0f, (hD - hU) / dz);
            return Vector3.Normalize(n);
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
                        int idx = (int)((x + width * z) * 4);
                        byte baseVal = image.Data[idx];

                        float r = baseVal + 0;
                        float g = baseVal + 1;
                        float b = baseVal + 2;

                        rawHeightmap[x, z] = ((r + g + b) / 3.0f) * 4f;
                    }
                }

                int radius = 4;
                float[,] tempMap = new float[width, length];

                Parallel.For(0, (int)length, z =>
                {
                    for (int x = 0; x < width; x++)
                    {
                        float sum = 0f;
                        int count = 0;

                        for (int i = -radius; i <= radius; i++)
                        {
                            long nx = x + i;
                            if (nx >= 0 && nx < width)
                            {
                                sum += rawHeightmap[nx, z];
                                count++;
                            }
                        }
                        tempMap[x, z] = sum / count;
                    }
                });

                Parallel.For(0, (int)width, x =>
                {
                    for (int z = 0; z < length; z++)
                    {
                        float sum = 0f;
                        int count = 0;

                        for (int j = -radius; j <= radius; j++)
                        {
                            long nz = z + j;
                            if (nz >= 0 && nz < length)
                            {
                                sum += tempMap[x, nz];
                                count++;
                            }
                        }
                        heightmap[x, z] = sum / count;
                    }
                });
            }

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

    public struct FrustumPlane
    {
        public Vector3 Normal;
        public float D;

        public void Set(float a, float b, float c, float d)
        {
            float length = (float)Math.Sqrt(a * a + b * b + c * c);
            Normal = new Vector3(a / length, b / length, c / length);
            D = d / length;
        }

        public float SignedDistanceTo(Vector3 point)
        {
            return Vector3.Dot(Normal, point) + D;
        }
    }

    public class Frustum
    {
        private FrustumPlane[] planes = new FrustumPlane[6];

        public void Update(Matrix4 viewProj)
        {
            // Left
            planes[0].Set(
                viewProj.M14 + viewProj.M11,
                viewProj.M24 + viewProj.M21,
                viewProj.M34 + viewProj.M31,
                viewProj.M44 + viewProj.M41);

            // Right
            planes[1].Set(
                viewProj.M14 - viewProj.M11,
                viewProj.M24 - viewProj.M21,
                viewProj.M34 - viewProj.M31,
                viewProj.M44 - viewProj.M41);

            // Bottom
            planes[2].Set(
                viewProj.M14 + viewProj.M12,
                viewProj.M24 + viewProj.M22,
                viewProj.M34 + viewProj.M32,
                viewProj.M44 + viewProj.M42);

            // Top
            planes[3].Set(
                viewProj.M14 - viewProj.M12,
                viewProj.M24 - viewProj.M22,
                viewProj.M34 - viewProj.M32,
                viewProj.M44 - viewProj.M42);

            // Near
            planes[4].Set(
                viewProj.M14 + viewProj.M13,
                viewProj.M24 + viewProj.M23,
                viewProj.M34 + viewProj.M33,
                viewProj.M44 + viewProj.M43);

            // Far
            planes[5].Set(
                viewProj.M14 - viewProj.M13,
                viewProj.M24 - viewProj.M23,
                viewProj.M34 - viewProj.M33,
                viewProj.M44 - viewProj.M43);
        }

        public bool IsSphereVisible(Vector3 center, float radius)
        {
            for (int i = 0; i < 6; i++)
            {
                if (planes[i].SignedDistanceTo(center) < -radius)
                {
                    return false;
                }
            }
            return true;
        }
    }

    public class TerrainChunk
    {
        private int vao, vbo, ebo;
        private int indexCount;
        private Matrix4 model;
        private Vector3 sphereCenter;
        private float sphereRadius;

        public TerrainChunk(uint startX, uint startZ, uint size, float[,] hm)
        {
            List<float> vertexdata = new List<float>();
            List<uint> indices = new List<uint>();

            uint endX = Math.Min(startX + size, Terrain.width - 1);
            uint endZ = Math.Min(startZ + size, Terrain.length - 1);

            uint spanX = (endX - startX) + 1;
            uint spanZ = (endZ - startZ) + 1;

            vertexdata.Capacity = (int)(spanX * spanZ * 8);
            indices.Capacity = (int)((spanX - 1) * (spanZ - 1) * 6);

            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;

            List<Vector3> worldVertices = new List<Vector3>((int)(spanX * spanZ));

            for (uint x = startX; x <= endX; x++)
            {
                for (uint z = startZ; z <= endZ; z++)
                {
                    float h = hm[x, z];
                    float xpos = (-Terrain.width / 2.0f) + x;
                    float zpos = (-Terrain.length / 2.0f) + z;

                    if (xpos < minX) minX = xpos;
                    if (xpos > maxX) maxX = xpos;
                    if (h < minY) minY = h;
                    if (h > maxY) maxY = h;
                    if (zpos < minZ) minZ = zpos;
                    if (zpos > maxZ) maxZ = zpos;

                    Vector3 worldPos = new Vector3(xpos, h + Terrain.OffsetY, zpos);
                    worldVertices.Add(worldPos);

                    Vector3 n = Terrain.GetNormalAt((int)x, (int)z);

                    vertexdata.Add(xpos);
                    vertexdata.Add(h);
                    vertexdata.Add(zpos);

                    vertexdata.Add((1.0f / 2048f) * x);
                    vertexdata.Add((1.0f / 2048f) * z);

                    vertexdata.Add(n.X);
                    vertexdata.Add(n.Y);
                    vertexdata.Add(n.Z);
                }
            }

            // Calculate precise bounding sphere for robust frustum culling
            Vector3 minBounds = new Vector3(minX, minY + Terrain.OffsetY, minZ);
            Vector3 maxBounds = new Vector3(maxX, maxY + Terrain.OffsetY, maxZ);
            sphereCenter = (minBounds + maxBounds) * 0.5f;

            sphereRadius = 0f;
            for (int i = 0; i < worldVertices.Count; i++)
            {
                float dist = Vector3.Distance(sphereCenter, worldVertices[i]);
                if (dist > sphereRadius)
                {
                    sphereRadius = dist;
                }
            }

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
            int stride = 8 * sizeof(float);

            vao = GL.GenVertexArray();
            vbo = GL.GenBuffer();
            ebo = GL.GenBuffer();

            GL.BindVertexArray(vao);

            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            float[] _vbo = vertexdata.ToArray();
            GL.BufferData(BufferTarget.ArrayBuffer, sizeof(float) * _vbo.Length, _vbo, BufferUsageHint.StaticDraw);

            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);

            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));

            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, stride, 5 * sizeof(float));

            GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
            uint[] _ebo = indices.ToArray();
            GL.BufferData(BufferTarget.ElementArrayBuffer, _ebo.Length * sizeof(uint), _ebo, BufferUsageHint.StaticDraw);

            GL.BindVertexArray(0);
        }

        public bool IsInFrustum(Frustum frustum)
        {
            return frustum.IsSphereVisible(sphereCenter, sphereRadius);
        }

        public void Draw(int modelLoc)
        {
            GL.UniformMatrix4(modelLoc, true, ref model);

            GL.BindVertexArray(vao);
            GL.DrawElements(OpenTK.Graphics.OpenGL4.PrimitiveType.Triangles, indexCount, DrawElementsType.UnsignedInt, 0);
            GL.BindVertexArray(0);
        }
    }
}