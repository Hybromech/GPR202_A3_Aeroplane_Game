using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
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

        private const float HeightScale = 4.0f; //4.0f
        private const uint CHUNK_SIZE = 128;

        private static float[,] heightmap;
        private static float[] heightmapFlat;
        private static Vector3[] normalsFlat;

        private List<TerrainChunk> chunks;
        private int textureHandle;
        private int terrainShaderProgram;
        private Frustum frustum;

        // Cached uniform locations
        private int modelLocation;
        private int viewLocation;
        private int projectionLocation;
        private int lightSpaceLocation;
        private int texture0Location;
        private int shadowMapLocation;

        string vertexShaderCode =
            @"
                #version 330 core

                layout (location = 0) in vec3 aPosition;
                layout (location = 1) in vec2 aTexCoord;
                layout (location = 2) in vec3 aNormal;

                uniform mat4 model;
                uniform mat4 view;
                uniform mat4 projection;
                uniform mat4 lightSpaceMatrix;

                out vec2 texCoord;
                out vec3 fragPos;
                out vec3 normal;
                out vec4 lightSpacePos;

                void main()
                {
                    texCoord = aTexCoord;
                    vec4 worldPosition = vec4(aPosition, 1.0f) * model;
                    fragPos = worldPosition.xyz;
                    normal = normalize(aNormal * mat3(model));
                    lightSpacePos = worldPosition * lightSpaceMatrix;
                    gl_Position = worldPosition * view * projection;
                }";

        string fragShaderCode =
            @"
                #version 330 core

                in vec2 texCoord;
                in vec3 fragPos;
                in vec3 normal;
                in vec4 lightSpacePos;

                out vec4 pixelColor;

                struct DirLight {
                    vec3 direction;
                    vec3 ambient;
                    vec3 diffuse;
                    vec3 specular;
                };

                uniform sampler2D texture0;
                uniform sampler2D shadowMap;
                uniform DirLight dirLight;
                uniform vec3 viewPos;

                float ComputeShadow(vec4 lsPos, vec3 norm, vec3 lightDir)
                {
                    vec3 projCoords = lsPos.xyz / lsPos.w;
                    projCoords = projCoords * 0.5 + 0.5;

                    if (projCoords.z > 1.0 || projCoords.x < 0.0 || projCoords.x > 1.0 || projCoords.y < 0.0 || projCoords.y > 1.0)
                    {
                        return 0.0;
                    }

                    float bias = max(0.0012 * (1.0 - dot(norm, lightDir)), 0.00025);
                    vec2 texelSize = 1.0 / textureSize(shadowMap, 0);

                    vec2 offsets[9] = vec2[](
                        vec2(-1.0, -1.0), vec2( 0.0, -1.0), vec2( 1.0, -1.0),
                        vec2(-1.0,  0.0), vec2( 0.0,  0.0), vec2( 1.0,  0.0),
                        vec2(-1.0,  1.0), vec2( 0.0,  1.0), vec2( 1.0,  1.0)
                    );

                    float shadow = 0.0;
                    for (int i = 0; i < 9; i++)
                    {
                        float pcfDepth = texture(shadowMap, projCoords.xy + offsets[i] * texelSize).r;
                        float diff = (projCoords.z - bias) - pcfDepth;
                        shadow += smoothstep(0.0, 0.0008, diff);
                    }

                    return shadow / 9.0;
                }

                void main()
                {
                    vec3 baseColor = texture(texture0, texCoord).rgb;
                    vec3 norm = normalize(normal);
                    vec3 lightDir = normalize(-dirLight.direction);
                    vec3 viewDir = normalize(viewPos - fragPos);

                    float diff = max(dot(norm, lightDir), 0.0);

                    vec3 halfwayDir = normalize(lightDir + viewDir);
                    float spec = pow(max(dot(norm, halfwayDir), 0.0), 32.0);

                    float shadow = ComputeShadow(lightSpacePos, norm, lightDir);

                    vec3 ambient  = dirLight.ambient * baseColor;
                    vec3 diffuse  = dirLight.diffuse * diff * baseColor * (1.0 - shadow);
                    vec3 specular = dirLight.specular * spec * baseColor * (1.0 - shadow);

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

        public void DrawDepth(ShadowMap shadowMap)
        {
            if (shadowMap == null)
            {
                return;
            }

            for (int i = 0; i < chunks.Count; i++)
            {
                chunks[i].DrawDepth(shadowMap);
            }
        }

        public void Draw(Matrix4 view, Matrix4 proj, DirectionalLight sunLight, Vector3 viewPos, ShadowMap shadowMap = null)
        {
            GL.UseProgram(terrainShaderProgram);

            sunLight?.ApplyToShader(terrainShaderProgram, viewPos);

            // Upload view and projection matrices ONCE per frame
            GL.UniformMatrix4(viewLocation, true, ref view);
            GL.UniformMatrix4(projectionLocation, true, ref proj);

            if (texture0Location != -1)
            {
                GL.Uniform1(texture0Location, 0);
            }

            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, textureHandle);

            if (shadowMap != null)
            {
                if (lightSpaceLocation != -1)
                {
                    Matrix4 lsMatrix = shadowMap.LightSpaceMatrix;
                    GL.UniformMatrix4(lightSpaceLocation, true, ref lsMatrix);
                }

                if (shadowMapLocation != -1)
                {
                    GL.Uniform1(shadowMapLocation, 1);
                }

                shadowMap.BindDepthMap(TextureUnit.Texture1);
            }

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
            x = Math.Clamp(x, 0, (int)width - 1);
            z = Math.Clamp(z, 0, (int)length - 1);
            return heightmapFlat[z * (int)width + x];
        }

        public static Vector3 GetNormalAt(int x, int z)
        {
            x = Math.Clamp(x, 0, (int)width - 1);
            z = Math.Clamp(z, 0, (int)length - 1);
            return normalsFlat[z * (int)width + x];
        }

        public static Vector3 WorldPositionFromHeightMap(int x, int z)
        {
            return new Vector3(x - ((int)width / 2), OffsetY + GetHeightMapAt(x, z), z - ((int)length / 2));
        }

        public static float GetInterpolatedHeightAtWorld(float worldX, float worldZ)
        {
            int w = (int)width;
            int l = (int)length;

            float gx = Math.Clamp(worldX + (w * 0.5f), 0f, w - 1.0001f);
            float gz = Math.Clamp(worldZ + (l * 0.5f), 0f, l - 1.0001f);

            int x0 = (int)gx;
            int z0 = (int)gz;
            int x1 = x0 + 1;
            int z1 = z0 + 1;

            float tx = gx - x0;
            float tz = gz - z0;

            int row0 = z0 * w;
            int row1 = z1 * w;

            float h00 = heightmapFlat[row0 + x0];
            float h10 = heightmapFlat[row0 + x1];
            float h01 = heightmapFlat[row1 + x0];
            float h11 = heightmapFlat[row1 + x1];

            float hx0 = h00 + (h10 - h00) * tx;
            float hx1 = h01 + (h11 - h01) * tx;

            return OffsetY + (hx0 + (hx1 - hx0) * tz);
        }

        private void ImportHeightMap()
        {
            string hm_fullpath = "Resource/Terrain/heightmap.png";
            if (!File.Exists(hm_fullpath))
            {
                throw new FileNotFoundException("Heightmap not found at '" + hm_fullpath + "'");
            }

            int w = (int)width;
            int l = (int)length;
            int totalPixels = w * l;

            float[] rawHeightmap = new float[totalPixels];
            float[] tempMap = new float[totalPixels];
            heightmapFlat = new float[totalPixels];
            normalsFlat = new Vector3[totalPixels];

            byte[] rgbaData;
            using (var stream = File.OpenRead(hm_fullpath))
            {
                ImageResult image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
                rgbaData = image.Data;
            }

            // 1. Parallel pixel-to-height extraction
            Parallel.For(0, l, z =>
            {
                int rowOffset = z * w;
                int byteRowOffset = rowOffset * 4;

                for (int x = 0; x < w; x++)
                {
                    int bIdx = byteRowOffset + (x * 4);
                    float avg = (rgbaData[bIdx] + rgbaData[bIdx + 1] + rgbaData[bIdx + 2]) * (1f / 3f);
                    rawHeightmap[rowOffset + x] = avg * HeightScale;
                }
            });

            // Kernel weights [1, 2, 4, 2, 1] / 10f
            const float w0 = 0.1f; // offset -2, +2
            const float w1 = 0.2f; // offset -1, +1
            const float w2 = 0.4f; // offset  0

            // 2. Horizontal pass (fast interior without per-pixel bounds checks)
            Parallel.For(0, l, z =>
            {
                int row = z * w;

                for (int x = 0; x < w; x++)
                {
                    if (x >= 2 && x < w - 2)
                    {
                        tempMap[row + x] =
                            (rawHeightmap[row + x - 2] * w0) +
                            (rawHeightmap[row + x - 1] * w1) +
                            (rawHeightmap[row + x] * w2) +
                            (rawHeightmap[row + x + 1] * w1) +
                            (rawHeightmap[row + x + 2] * w0);
                    }
                    else
                    {
                        float sum = 0f;
                        float weightSum = 0f;
                        for (int k = -2; k <= 2; k++)
                        {
                            int nx = x + k;
                            if ((uint)nx < (uint)w)
                            {
                                float kw = (k == 0) ? w2 : (Math.Abs(k) == 1 ? w1 : w0);
                                sum += rawHeightmap[row + nx] * kw;
                                weightSum += kw;
                            }
                        }
                        tempMap[row + x] = sum / weightSum;
                    }
                }
            });

            // 3. Vertical pass (fast interior rows 2..l-3)
            Parallel.For(0, l, z =>
            {
                int row = z * w;

                if (z >= 2 && z < l - 2)
                {
                    int rM2 = (z - 2) * w;
                    int rM1 = (z - 1) * w;
                    int rP1 = (z + 1) * w;
                    int rP2 = (z + 2) * w;

                    for (int x = 0; x < w; x++)
                    {
                        heightmapFlat[row + x] =
                            (tempMap[rM2 + x] * w0) +
                            (tempMap[rM1 + x] * w1) +
                            (tempMap[row + x] * w2) +
                            (tempMap[rP1 + x] * w1) +
                            (tempMap[rP2 + x] * w0);
                    }
                }
                else
                {
                    for (int x = 0; x < w; x++)
                    {
                        float sum = 0f;
                        float weightSum = 0f;
                        for (int k = -2; k <= 2; k++)
                        {
                            int nz = z + k;
                            if ((uint)nz < (uint)l)
                            {
                                float kw = (k == 0) ? w2 : (Math.Abs(k) == 1 ? w1 : w0);
                                sum += tempMap[nz * w + x] * kw;
                                weightSum += kw;
                            }
                        }
                        heightmapFlat[row + x] = sum / weightSum;
                    }
                }
            });

            // 4. Sync 2D heightmap array so TerrainChunk and legacy callers stay populated
            Parallel.For(0, l, z =>
            {
                int row = z * w;
                for (int x = 0; x < w; x++)
                {
                    heightmap[x, z] = heightmapFlat[row + x];
                }
            });

            // 5. Precompute smooth vertex normals once in parallel
            Parallel.For(0, l, z =>
            {
                int zD = z > 0 ? z - 1 : 0;
                int zU = z < l - 1 ? z + 1 : l - 1;
                float invDz = 1f / Math.Max(1, zU - zD);
                int row = z * w;

                for (int x = 0; x < w; x++)
                {
                    int xL = x > 0 ? x - 1 : 0;
                    int xR = x < w - 1 ? x + 1 : w - 1;
                    float invDx = 1f / Math.Max(1, xR - xL);

                    float hL = heightmapFlat[row + xL];
                    float hR = heightmapFlat[row + xR];
                    float hD = heightmapFlat[zD * w + x];
                    float hU = heightmapFlat[zU * w + x];

                    Vector3 n = new Vector3((hL - hR) * invDx, 2.0f, (hD - hU) * invDz);
                    normalsFlat[row + x] = Vector3.Normalize(n);
                }
            });

            // Load diffuse texture
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
                GL.TexImage2D(
                    TextureTarget.Texture2D,
                    0,
                    PixelInternalFormat.Rgba,
                    image.Width,
                    image.Height,
                    0,
                    PixelFormat.Rgba,
                    PixelType.UnsignedByte,
                    image.Data);
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
            lightSpaceLocation = GL.GetUniformLocation(terrainShaderProgram, "lightSpaceMatrix");
            texture0Location = GL.GetUniformLocation(terrainShaderProgram, "texture0");
            shadowMapLocation = GL.GetUniformLocation(terrainShaderProgram, "shadowMap");
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

        public void DrawDepth(ShadowMap shadowMap)
        {
            Matrix4 chunkModel = model;
            shadowMap.ApplyDepthModel(chunkModel);

            GL.BindVertexArray(vao);
            GL.DrawElements(OpenTK.Graphics.OpenGL4.PrimitiveType.Triangles, indexCount, DrawElementsType.UnsignedInt, 0);
            GL.BindVertexArray(0);
        }

        public void Draw(int modelLoc)
        {
            Matrix4 chunkModel = model;
            GL.UniformMatrix4(modelLoc, true, ref chunkModel);

            GL.BindVertexArray(vao);
            GL.DrawElements(OpenTK.Graphics.OpenGL4.PrimitiveType.Triangles, indexCount, DrawElementsType.UnsignedInt, 0);
            GL.BindVertexArray(0);
        }
    }
}
