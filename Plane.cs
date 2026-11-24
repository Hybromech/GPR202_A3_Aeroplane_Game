using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Assimp;
using Assimp.Configs;
using Assimp.Unmanaged;
using OpenTK;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;
using StbImageSharp;

namespace GPR202_Assessment3
{
    public class Plane : GameObject
    {
        List<VertexData> vertexdata;
        List<uint> indices;
        private readonly List<Vector3> normals = new();

        private Vector3 up = Vector3.UnitY;
        public Vector3 Up => up;

        private Vector3 front = -Vector3.UnitZ;
        public Vector3 Front => front;

        private Vector3 right = Vector3.UnitX;
        public Vector3 Right => right;

        private float _pitch;
        public float Pitch
        {
            get => _pitch;
            set
            {
                if (value > 89.0f)
                {
                    _pitch = 89.0f;
                }
                else if (value <= -89.0f)
                {
                    _pitch = -89.0f;
                }
                else
                {
                    _pitch = value;
                }
                UpdateVertices();
            }
        }

        private float yaw;
        public float Yaw
        {
            get => yaw;
            set
            {
                yaw = value;
                UpdateVertices();
            }
        }

        public Vector3 Direction => (position + Front);

        private int planeShaderProgram;

        // PERFORMANCE OPTIMIZATION: Cached uniform locations.
        private int modelLocation;
        private int viewLocation;
        private int projectionLocation;
        private int lightSpaceLocation;
        private int texture0Location;
        private int shadowMapLocation;
        private int biasLocation;
        private int pcfSpreadLocation;
        private int softEdgeRangeLocation;
        private int shadowStrengthLocation;
        private int bloomThresholdLocation;
        private int bloomIntensityLocation;

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
                out vec3 worldNormal;
                out vec4 lightSpacePos;

                void main()
                {
                    texCoord = aTexCoord;
                    vec4 worldPosition = vec4(aPosition, 1.0f) * model;
                    fragPos = worldPosition.xyz;
                    worldNormal = normalize(aNormal * transpose(inverse(mat3(model))));
                    lightSpacePos = worldPosition * lightSpaceMatrix;
                    gl_Position = worldPosition * view * projection;
                }";

        string fragShaderCode =
        @"
                #version 330 core

                out vec4 pixelColor;

                in vec2 texCoord;
                in vec3 fragPos;
                in vec3 worldNormal;
                in vec4 lightSpacePos;

                struct DirLight {
                    vec3 direction;
                    vec3 ambient;
                    vec3 diffuse;
                    vec3 specular;
                };

                uniform sampler2D texture0;
                uniform sampler2D shadowMap;
                uniform DirLight dirLight;
                uniform vec3 ambientLight;
                uniform vec3 viewPos;

                uniform float uMetalness;
                uniform float uRoughness;
                uniform float uBias;
                uniform float uPcfSpread;
                uniform float uSoftEdgeRange;
                uniform float uShadowStrength;
                uniform float uBloomThreshold;
                uniform float uBloomIntensity;

                float CompareShadowTexel(ivec2 texel, float receiverDepth)
                {
                    ivec2 size = textureSize(shadowMap, 0);
                    // texelFetch does not apply ClampToBorder; outside the map is lit.
                    if (any(lessThan(texel, ivec2(0))) || any(greaterThanEqual(texel, size)))
                        return 0.0;

                    float depth = texelFetch(shadowMap, texel, 0).r;
                    return smoothstep(0.0, max(uSoftEdgeRange, 0.00001), receiverDepth - depth);
                }

                float FilteredShadow(vec2 uv, float receiverDepth)
                {
                    // Interpolate comparisons, never depths, to avoid false occluders.
                    // Subtract half a texel so integer coordinates are texel centers.
                    vec2 position = uv * vec2(textureSize(shadowMap, 0)) - vec2(0.5);
                    ivec2 base = ivec2(floor(position));
                    vec2 weight = fract(position);

                    float bottom = mix(
                        CompareShadowTexel(base, receiverDepth),
                        CompareShadowTexel(base + ivec2(1, 0), receiverDepth), weight.x);
                    float top = mix(
                        CompareShadowTexel(base + ivec2(0, 1), receiverDepth),
                        CompareShadowTexel(base + ivec2(1, 1), receiverDepth), weight.x);
                    return mix(bottom, top, weight.y);
                }

                float ComputeShadow(vec4 lsPos, vec3 lightDir)
                {
                    vec3 projCoords = lsPos.xyz / lsPos.w;
                    projCoords = projCoords * 0.5 + 0.5;

                    if (projCoords.z < 0.0 || projCoords.z > 1.0 || projCoords.x < 0.0 || projCoords.x > 1.0 || projCoords.y < 0.0 || projCoords.y > 1.0)
                    {
                        return 0.0;
                    }

                    vec2 texelSize = (1.0 / textureSize(shadowMap, 0)) * uPcfSpread;

                    vec2 offsets[9] = vec2[](
                        vec2(-1.0, -1.0), vec2( 0.0, -1.0), vec2( 1.0, -1.0),
                        vec2(-1.0,  0.0), vec2( 0.0,  0.0), vec2( 1.0,  0.0),
                        vec2(-1.0,  1.0), vec2( 0.0,  1.0), vec2( 1.0,  1.0)
                    );

                    float shadow = 0.0;
                    for (int i = 0; i < 9; i++)
                    {
                        shadow += FilteredShadow(projCoords.xy + offsets[i] * texelSize,
                            projCoords.z - uBias);
                    }

                    return shadow / 9.0;
                }

                void main()
                {
                    vec4 texColor = texture(texture0, texCoord);
                    vec3 baseColor = texColor.rgb;
                    vec3 lightDir = normalize(-dirLight.direction);

                    float shadow = ComputeShadow(lightSpacePos, lightDir);
                    float strength = uShadowStrength;

                    // Cook-Torrance metallic BRDF: GGX distribution, Schlick Fresnel/geometry.
                    const float PI = 3.14159265359;
                    vec3 N = normalize(worldNormal);
                    if (!gl_FrontFacing) N = -N;
                    vec3 V = normalize(viewPos - fragPos);
                    vec3 H = lightDir + V;
                    H = dot(H, H) > 0.000001 ? normalize(H) : N;
                    float NoL = max(dot(N, lightDir), 0.0);
                    float NoV = max(dot(N, V), 0.0001);
                    float NoH = max(dot(N, H), 0.0);
                    vec3 F0 = mix(vec3(0.04), baseColor, uMetalness);
                    vec3 F = F0 + (1.0 - F0) * pow(1.0 - max(dot(H, V), 0.0), 5.0);
                    float a = uRoughness * uRoughness;
                    float a2 = a * a;
                    float d = NoH * NoH * (a2 - 1.0) + 1.0;
                    float D = a2 / max(PI * d * d, 0.0000001);
                    float k = (uRoughness + 1.0) * (uRoughness + 1.0) / 8.0;
                    float G = (NoV / (NoV * (1.0 - k) + k))
                        * (NoL / (NoL * (1.0 - k) + k));
                    vec3 specular = D * G * F / max(4.0 * NoV * NoL, 0.0001);
                    vec3 diffuse = (1.0 - F) * (1.0 - uMetalness) * baseColor / PI;
                    // Ambient fill is an approximation; no environment reflection map is sampled.
                    vec3 ambient = ambientLight * mix(baseColor, F0, uMetalness);
                    vec3 litColor = ambient + (diffuse + specular) * dirLight.diffuse
                        * NoL * (1.0 - shadow * strength);

                    // Raise only bright, already shaded regions into HDR for bloom.
                    float luminance = dot(litColor, vec3(0.2126, 0.7152, 0.0722));
                    float highlight = smoothstep(uBloomThreshold, uBloomThreshold + 0.1, luminance);
                    litColor *= 1.0 + highlight * uBloomIntensity;
                    pixelColor = vec4(litColor, texColor.a);
                }
            ";

        public Plane()
        {
            ImportModelFile();
            Setup();
        }

        private bool firstMove = true;
        private Vector2 lastPos;
        public float FlySpeed = 35.0f; //35.0f
        public float TurnSpeed = 256.0f;
        public float LiftSpeed = 256.0f; // 256.0f
        public float Turn = 0f;
        public float Lift = 0f;

        public void OnFrameUpdate(MouseState mouse, int viewportW, int viewportH, double deltaT)
        {
            if (firstMove) // this bool variable is initially set to true
            {
                lastPos = new Vector2(mouse.X, mouse.Y);
                firstMove = false;
            }
            else
            {
                //Calculate the offset of the mouse position
                float deltaX = mouse.X - lastPos.X;
                float deltaY = mouse.Y - lastPos.Y;
                lastPos = new Vector2(mouse.X, mouse.Y);

                Turn = ((2.0f / viewportW) * mouse.X) - 1.0f;
                Lift = ((2.0f / viewportH) * mouse.Y) - 1.0f;
                position += new Vector3(0f, Lift * 0.1f, 0f) * LiftSpeed * (float)deltaT;
            }
        }

        public void UpdateVertices()
        {
            // PERFORMANCE OPTIMIZATION: Convert degrees to radians once and cache trig results
            float pitchRad = MathHelper.DegreesToRadians(Pitch);
            float yawRad = MathHelper.DegreesToRadians(Yaw);

            float cosPitch = (float)Math.Cos(pitchRad);
            float sinPitch = (float)Math.Sin(pitchRad);
            float cosYaw = (float)Math.Cos(yawRad);
            float sinYaw = (float)Math.Sin(yawRad);

            front.X = cosPitch * cosYaw;
            front.Y = sinPitch;
            front.Z = cosPitch * sinYaw;
            front = Vector3.Normalize(front);

            right = Vector3.Normalize(Vector3.Cross(front, Vector3.UnitY));
            up = Vector3.Normalize(Vector3.Cross(right, front));
        }

        private Matrix4 ComputeModelMatrix()
        {
            return Matrix4.Identity
                * Matrix4.CreateScale(scale)
                * Matrix4.CreateRotationY(rotation.Y)
                * Matrix4.CreateRotationX(rotation.X)
                * Matrix4.CreateRotationZ(rotation.Z)
                * Matrix4.CreateTranslation(position);
        }

        public void DrawDepth(ShadowMap shadowMap)
        {
            if (shadowMap == null)
            {
                return;
            }

            model = ComputeModelMatrix();
            shadowMap.ApplyDepthModel(model);

            GL.BindVertexArray(vao);
            GL.DrawElements(OpenTK.Graphics.OpenGL4.PrimitiveType.Triangles, indices.Count, DrawElementsType.UnsignedInt, 0);
            GL.BindVertexArray(0);
        }

        public void Draw(Matrix4 view, Matrix4 proj, DirectionalLight sunLight, Vector3 viewPos, AmbientLight ambientLight, Material material, ShadowMap shadowMap = null)
        {
            model = ComputeModelMatrix();

            GL.UseProgram(planeShaderProgram);

            sunLight?.ApplyToShader(planeShaderProgram, viewPos);
            ambientLight.ApplyToShader(planeShaderProgram);
            material.ApplyToShader(planeShaderProgram);

            // Reset every draw, including draws without a shadow map/settings source.
            ShadowSettings? bloomSettings = shadowMap?.Settings;
            bool bloomActive = bloomSettings != null && bloomSettings.BloomEnabled
                && bloomSettings.BloomStrength > 0f && bloomSettings.PlaneBloomEnabled;
            GL.Uniform1(bloomThresholdLocation, Math.Max(0f, bloomSettings?.PlaneBloomThreshold ?? 0.6f));
            GL.Uniform1(bloomIntensityLocation, bloomActive
                ? Math.Max(0f, bloomSettings!.PlaneBloomIntensity) : 0f);

            // PERFORMANCE OPTIMIZATION: Use cached uniform locations instead of GL.GetUniformLocation every frame.
            GL.UniformMatrix4(modelLocation, true, ref model);
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

                ShadowSettings cfg = shadowMap.Settings;
                if (biasLocation != -1) GL.Uniform1(biasLocation, cfg.PlaneBias);
                if (pcfSpreadLocation != -1) GL.Uniform1(pcfSpreadLocation, cfg.PlanePcfSpread);
                if (softEdgeRangeLocation != -1) GL.Uniform1(softEdgeRangeLocation, cfg.PlaneSoftEdgeRange);
                if (shadowStrengthLocation != -1) GL.Uniform1(shadowStrengthLocation, cfg.PlaneShadowStrength);

                shadowMap.BindDepthMap(TextureUnit.Texture1);
            }

            GL.BindVertexArray(vao);
            GL.DrawElements(OpenTK.Graphics.OpenGL4.PrimitiveType.Triangles, indices.Count, DrawElementsType.UnsignedInt, 0);
            GL.BindVertexArray(0);
        }

        private void Setup()
        {
            // OpenGL Objects
            vbo = GL.GenBuffer();
            vao = GL.GenVertexArray();
            GL.BindVertexArray(vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);

            List<float> vbodata = new List<float>();
            for (int i = 0; i < vertexdata.Count; i++)
            {
                vbodata.Add(vertexdata[i].x);   // x position
                vbodata.Add(vertexdata[i].y);   // y position
                vbodata.Add(vertexdata[i].z);   // z position
                vbodata.Add(vertexdata[i].u);   // u texcoord
                vbodata.Add(vertexdata[i].v);   // v texcoord
                vbodata.Add(normals[i].X);
                vbodata.Add(normals[i].Y);
                vbodata.Add(normals[i].Z);
            }
            float[] _vbo = vbodata.ToArray();

            int stride = 8 * sizeof(float);
            GL.BufferData(BufferTarget.ArrayBuffer, sizeof(float) * _vbo.Length, _vbo, BufferUsageHint.StaticDraw);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, stride, 5 * sizeof(float));
            GL.EnableVertexAttribArray(2);

            ebo = GL.GenBuffer();
            uint[] _ebo = indices.ToArray();
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
            GL.BufferData(BufferTarget.ElementArrayBuffer, _ebo.Length * sizeof(uint), _ebo, BufferUsageHint.StaticDraw);

            GL.BindVertexArray(0);

            // Vertex and Fragment Shaders
            int vertexShaderObject = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vertexShaderObject, vertexShaderCode);
            GL.CompileShader(vertexShaderObject);

            string vertexShaderInfo = GL.GetShaderInfoLog(vertexShaderObject);
            if (vertexShaderInfo != string.Empty)
            {
                Console.WriteLine("PLANE VERT: " + vertexShaderInfo);
            }

            int fragmentShaderObject = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fragmentShaderObject, fragShaderCode);
            GL.CompileShader(fragmentShaderObject);

            string fragmentShaderInfo = GL.GetShaderInfoLog(fragmentShaderObject);
            if (fragmentShaderInfo != string.Empty)
            {
                Console.WriteLine("PLANE FRAG: " + fragmentShaderInfo);
            }

            // create shader program
            planeShaderProgram = GL.CreateProgram();
            GL.AttachShader(planeShaderProgram, vertexShaderObject);
            GL.AttachShader(planeShaderProgram, fragmentShaderObject);
            GL.LinkProgram(planeShaderProgram);

            GL.DetachShader(planeShaderProgram, vertexShaderObject);
            GL.DetachShader(planeShaderProgram, fragmentShaderObject);
            GL.DeleteShader(vertexShaderObject);
            GL.DeleteShader(fragmentShaderObject);

            // PERFORMANCE OPTIMIZATION: Cache uniform locations once after linking.
            modelLocation = GL.GetUniformLocation(planeShaderProgram, "model");
            viewLocation = GL.GetUniformLocation(planeShaderProgram, "view");
            projectionLocation = GL.GetUniformLocation(planeShaderProgram, "projection");
            lightSpaceLocation = GL.GetUniformLocation(planeShaderProgram, "lightSpaceMatrix");
            texture0Location = GL.GetUniformLocation(planeShaderProgram, "texture0");
            shadowMapLocation = GL.GetUniformLocation(planeShaderProgram, "shadowMap");
            biasLocation = GL.GetUniformLocation(planeShaderProgram, "uBias");
            pcfSpreadLocation = GL.GetUniformLocation(planeShaderProgram, "uPcfSpread");
            softEdgeRangeLocation = GL.GetUniformLocation(planeShaderProgram, "uSoftEdgeRange");
            shadowStrengthLocation = GL.GetUniformLocation(planeShaderProgram, "uShadowStrength");
            bloomThresholdLocation = GL.GetUniformLocation(planeShaderProgram, "uBloomThreshold");
            bloomIntensityLocation = GL.GetUniformLocation(planeShaderProgram, "uBloomIntensity");

            GL.UseProgram(planeShaderProgram);
        }

        private void ImportModelFile()
        {
            string modelName = "plane.obj";
            string fileName = "Resource/Model/" + modelName;

            //Create a new importer
            AssimpContext importer = new AssimpContext();

            //This is how we add a configuration (each config is its own class)
            NormalSmoothingAngleConfig config = new NormalSmoothingAngleConfig(66.0f);
            importer.SetConfig(config);

            //This is how we add a logging callback 
            LogStream logstream = new LogStream(delegate (String msg, String userData) {
                Console.WriteLine(msg);
            });
            logstream.Attach();

            //Import the model. All configs are set. The model
            //is imported, loaded into managed memory. Then the unmanaged memory is released, and everything is reset.
            Scene model = importer.ImportFile(fileName, PostProcessSteps.Triangulate | PostProcessSteps.GenerateSmoothNormals);

            // Load the model data into own structures
            vertexdata = new List<VertexData>();
            for (int i = 0; i < model.Meshes[0].VertexCount; i++)
            {
                VertexData vertex =
                    new VertexData(model.Meshes[0].Vertices[i].X, model.Meshes[0].Vertices[i].Y, model.Meshes[0].Vertices[i].Z,
                                       model.Meshes[0].TextureCoordinateChannels[0][i].X, model.Meshes[0].TextureCoordinateChannels[0][i].Y);
                vertexdata.Add(vertex);
                var normal = model.Meshes[0].Normals[i];
                normals.Add(new Vector3(normal.X, normal.Y, normal.Z));
            }

            indices = new List<uint>();
            for (int i = 0; i < model.Meshes[0].FaceCount; i++)
            {
                Assimp.Face face = model.Meshes[0].Faces[i];
                for (int j = 0; j < face.IndexCount; j++)
                {
                    indices.Add((uint)face.Indices[j]);
                }
            }

            // Load Texture
            textureHandle = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, textureHandle);

            StbImage.stbi_set_flip_vertically_on_load(1);
            string fullpath = "Resource/Model/BodyTexture.png";
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

            Console.WriteLine("Succesfully loaded " + modelName + " with " + model.Meshes[0].Vertices.Count + " vertices.");

            importer.Dispose();
        }
    }
}