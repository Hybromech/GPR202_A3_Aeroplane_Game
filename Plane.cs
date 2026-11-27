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
        public struct OrdnanceSlot
        {
            public int IndexOffset;
            public int IndexCount;
            public Vector3 LocalCenter;
            public bool Mounted;
        }

        public const int MissileCount = 6;
        public const int BombCount = 2;
        public const float DefaultMaxHealth = 100f;

        public float MaxHealth { get; private set; } = 500;
        public float Health { get; private set; } = 500;
        public bool IsDestroyed => Health <= 0f;
        public float HealthFraction => MaxHealth > 0f ? Math.Clamp(Health / MaxHealth, 0f, 1f) : 0f;

        public void SetHealth(float health, float? maxHealth = null)
        {
            float max = Math.Max(1f, maxHealth ?? health);
            MaxHealth = max;
            Health = Math.Clamp(health, 0f, max);
        }

        public void ResetHealth() => Health = MaxHealth;

        // Returns true on the hit that depletes health to 0 (destroying the plane).
        public bool TakeDamage(float damage)
        {
            if (IsDestroyed || damage <= 0f) return false;
            Health = Math.Max(0f, Health - damage);
            return IsDestroyed;
        }

        public void Destroy() => Health = 0f;

        // Two machine gun barrels per wing (4 total), matching plane.obj leading-edge guns.
        public static readonly Vector3[] WingGunMuzzles =
        {
            new Vector3(-2.77f, 0.50f, 0.98f),
            new Vector3(-2.31f, 0.50f, 1.00f),
            new Vector3( 2.24f, 0.48f, 0.90f),
            new Vector3( 2.70f, 0.48f, 0.86f)
        };

        List<VertexData> vertexdata;
        List<uint> indices;
        private int airframeIndexCount;
        private int propellerIndexCount;
        private readonly OrdnanceSlot[] missileSlots = new OrdnanceSlot[MissileCount];
        private readonly OrdnanceSlot[] bombSlots = new OrdnanceSlot[BombCount];
        private float propellerAngle;
        private static readonly Vector3 PropellerPivot = new Vector3(-0.00258f, 1.22923f, 3.4f);
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

        public Matrix4 PhysicsOrientation { get; set; } = Matrix4.CreateRotationY(MathF.PI);

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
        private int tintLocation;

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

                uniform bool uVictoryGold;
                uniform float uMetalness;
                uniform float uRoughness;
                uniform float uBias;
                uniform float uPcfSpread;
                uniform float uSoftEdgeRange;
                uniform float uShadowStrength;
                uniform float uBloomThreshold;
                uniform float uBloomIntensity;
                uniform vec3 uTint;

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

                float CompactShadow(vec2 uv, float receiverDepth)
                {
                    vec2 position = uv * vec2(textureSize(shadowMap, 0)) - vec2(0.5);
                    ivec2 first = ivec2(floor(position)) - ivec2(1);
                    vec2 f = fract(position);
                    float wx[4] = float[](1.0-f.x, 1.0, 1.0, f.x);
                    float wy[4] = float[](1.0-f.y, 1.0, 1.0, f.y);
                    float result = 0.0;
                    for (int y=0; y<4; y++)
                    for (int x=0; x<4; x++)
                        result += wx[x]*wy[y]*CompareShadowTexel(first+ivec2(x,y), receiverDepth);
                    return result / 9.0;
                }

                float ComputeShadow(vec4 lsPos, vec3 norm, vec3 lightDir)
                {
                    if (uShadowStrength <= 0.0) return 0.0;
                    vec3 projCoords = lsPos.xyz / lsPos.w;
                    projCoords = projCoords * 0.5 + 0.5;

                    if (projCoords.z < 0.0 || projCoords.z > 1.0 || projCoords.x < 0.0 || projCoords.x > 1.0 || projCoords.y < 0.0 || projCoords.y > 1.0)
                    {
                        return 0.0;
                    }

                    float ndotl = clamp(dot(norm, lightDir), 0.0, 1.0);
                    float bias = max(uBias * 2.5 * (1.0 - ndotl), uBias);

                    if (uPcfSpread == 1.0) return CompactShadow(projCoords.xy, projCoords.z - bias);

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
                            projCoords.z - bias);
                    }

                    return shadow / 9.0;
                }

                void main()
                {
                    vec4 texColor = texture(texture0, texCoord);
                    vec3 baseColor = (uVictoryGold ? vec3(1.0, 0.766, 0.336) : texColor.rgb) * uTint;
                    vec3 lightDir = normalize(-dirLight.direction);

                    // Cook-Torrance metallic BRDF: GGX distribution, Schlick Fresnel/geometry.
                    const float PI = 3.14159265359;
                    vec3 N = normalize(worldNormal);
                    if (!gl_FrontFacing) N = -N;
                    float shadow = ComputeShadow(lightSpacePos, N, lightDir);
                    float shadowFactor = shadow * uShadowStrength;

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
                    // Scale PBR diffuse to match the scene's linear directional light scale,
                    // and add a broad metallic sun sheen so high-metalness surfaces (including
                    // VictoryGold) respond clearly to directional light without an IBL cubemap.
                    vec3 diffuse = (1.0 - F) * (1.0 - uMetalness) * baseColor;
                    vec3 metalSheen = F0 * uMetalness * (0.55 + 0.45 * pow(NoH, 4.0));
                    vec3 ambientBase = ambientLight * mix(baseColor, F0, uMetalness);
                    float ambientVisibility = 1.0 - shadowFactor * 0.45;
                    vec3 ambient = ambientBase * ambientVisibility;
                    vec3 direct = (diffuse + metalSheen + specular) * dirLight.diffuse
                        * NoL * (1.0 - shadowFactor);
                    vec3 litColor = ambient + direct;

                    // Raise only bright, already shaded regions into HDR for bloom.
                    float luminance = dot(litColor, vec3(0.2126, 0.7152, 0.0722));
                    float highlight = smoothstep(uBloomThreshold, uBloomThreshold + 0.1, luminance);
                    litColor *= 1.0 + highlight * uBloomIntensity;
                    pixelColor = vec4(litColor, texColor.a);
                }
            ";

        public Plane()
        {
            MaxHealth = Math.Max(1f, Math.Max(MaxHealth, Health));
            Health = Math.Clamp(Health, 0f, MaxHealth);
            ImportModelFile();
            Setup();
        }

        public Plane(float maxHealth)
        {
            SetHealth(maxHealth, maxHealth);
            ImportModelFile();
            Setup();
        }

        private Plane(bool loadGraphics, float maxHealth)
        {
            SetHealth(maxHealth, maxHealth);
            if (loadGraphics)
            {
                ImportModelFile();
                Setup();
            }
        }

        // Creates a lightweight Plane instance (health/state only) for AI aircraft sharing the main GPU mesh.
        public static Plane CreateEntity(float maxHealth = DefaultMaxHealth) => new Plane(loadGraphics: false, maxHealth);

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

        public void UpdatePropeller(double deltaSeconds, float revolutionsPerMinute)
        {
            propellerAngle = (float)((propellerAngle + deltaSeconds * revolutionsPerMinute * Math.PI / 30.0)
                % (Math.PI * 2.0));
        }

        private Matrix4 ComputePropellerMatrix() =>
            Matrix4.CreateTranslation(-PropellerPivot)
            * Matrix4.CreateRotationZ(propellerAngle)
            * Matrix4.CreateTranslation(PropellerPivot)
            * ComputeModelMatrix();

        public Matrix4 ModelMatrix => ComputeModelMatrix();

        public OrdnanceSlot GetMissileSlot(int index) => missileSlots[index];
        public OrdnanceSlot GetBombSlot(int index) => bombSlots[index];
        public void SetMissileMounted(int index, bool mounted) => missileSlots[index].Mounted = mounted;
        public void SetBombMounted(int index, bool mounted) => bombSlots[index].Mounted = mounted;

        public void ResetOrdnance()
        {
            for (int i = 0; i < missileSlots.Length; i++) missileSlots[i].Mounted = true;
            for (int i = 0; i < bombSlots.Length; i++) bombSlots[i].Mounted = true;
        }

        private Matrix4 ComputeModelMatrix()
        {
            return Matrix4.Identity
                * Matrix4.CreateScale(scale)
                * PhysicsOrientation
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
            GL.DrawElements(OpenTK.Graphics.OpenGL4.PrimitiveType.Triangles, airframeIndexCount, DrawElementsType.UnsignedInt, 0);
            DrawMountedOrdnanceElements();
            shadowMap.ApplyDepthModel(ComputePropellerMatrix());
            GL.DrawElements(OpenTK.Graphics.OpenGL4.PrimitiveType.Triangles, propellerIndexCount, DrawElementsType.UnsignedInt, airframeIndexCount * sizeof(uint));
            GL.BindVertexArray(0);
        }

        public void DrawOrdnanceDepth(ShadowMap shadowMap, bool isBomb, int slotIndex, Vector3 worldPos, Matrix4 worldOrientation)
        {
            if (shadowMap == null) return;
            OrdnanceSlot slot = isBomb ? bombSlots[slotIndex] : missileSlots[slotIndex];
            if (slot.IndexCount <= 0) return;

            Matrix4 ordModel = Matrix4.CreateTranslation(-slot.LocalCenter)
                * worldOrientation
                * Matrix4.CreateTranslation(worldPos);
            shadowMap.ApplyDepthModel(ordModel);

            GL.BindVertexArray(vao);
            GL.DrawElements(OpenTK.Graphics.OpenGL4.PrimitiveType.Triangles, slot.IndexCount, DrawElementsType.UnsignedInt, slot.IndexOffset * sizeof(uint));
            GL.BindVertexArray(0);
        }

        private void DrawMountedOrdnanceElements()
        {
            for (int i = 0; i < missileSlots.Length; i++)
            {
                if (missileSlots[i].Mounted && missileSlots[i].IndexCount > 0)
                {
                    GL.DrawElements(OpenTK.Graphics.OpenGL4.PrimitiveType.Triangles, missileSlots[i].IndexCount, DrawElementsType.UnsignedInt, missileSlots[i].IndexOffset * sizeof(uint));
                }
            }
            for (int i = 0; i < bombSlots.Length; i++)
            {
                if (bombSlots[i].Mounted && bombSlots[i].IndexCount > 0)
                {
                    GL.DrawElements(OpenTK.Graphics.OpenGL4.PrimitiveType.Triangles, bombSlots[i].IndexCount, DrawElementsType.UnsignedInt, bombSlots[i].IndexOffset * sizeof(uint));
                }
            }
        }

        private void BindPlaneShaderAndState(Matrix4 view, Matrix4 proj, DirectionalLight sunLight, Vector3 viewPos, AmbientLight ambientLight, Material material, ShadowMap? shadowMap, Vector3? tint = null)
        {
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
            if (tintLocation != -1)
            {
                Vector3 t = tint ?? Vector3.One;
                GL.Uniform3(tintLocation, t);
            }

            // PERFORMANCE OPTIMIZATION: Use cached uniform locations instead of GL.GetUniformLocation every frame.
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
        }

        public void Draw(Matrix4 view, Matrix4 proj, DirectionalLight sunLight, Vector3 viewPos, AmbientLight ambientLight, Material material, ShadowMap shadowMap = null)
        {
            model = ComputeModelMatrix();
            BindPlaneShaderAndState(view, proj, sunLight, viewPos, ambientLight, material, shadowMap);
            GL.UniformMatrix4(modelLocation, true, ref model);

            GL.BindVertexArray(vao);
            GL.DrawElements(OpenTK.Graphics.OpenGL4.PrimitiveType.Triangles, airframeIndexCount, DrawElementsType.UnsignedInt, 0);
            DrawMountedOrdnanceElements();
            Matrix4 propellerModel = ComputePropellerMatrix();
            GL.UniformMatrix4(modelLocation, true, ref propellerModel);
            GL.DrawElements(OpenTK.Graphics.OpenGL4.PrimitiveType.Triangles, propellerIndexCount, DrawElementsType.UnsignedInt, airframeIndexCount * sizeof(uint));
            GL.BindVertexArray(0);
        }

        public void DrawOrdnance(bool isBomb, int slotIndex, Vector3 worldPos, Matrix4 worldOrientation, Matrix4 view, Matrix4 proj, DirectionalLight sunLight, Vector3 viewPos, AmbientLight ambientLight, Material material, ShadowMap? shadowMap = null, Vector3? tint = null)
        {
            OrdnanceSlot slot = isBomb ? bombSlots[slotIndex] : missileSlots[slotIndex];
            if (slot.IndexCount <= 0) return;

            BindPlaneShaderAndState(view, proj, sunLight, viewPos, ambientLight, material, shadowMap, tint);
            Matrix4 ordModel = Matrix4.CreateTranslation(-slot.LocalCenter)
                * worldOrientation
                * Matrix4.CreateTranslation(worldPos);
            GL.UniformMatrix4(modelLocation, true, ref ordModel);

            GL.BindVertexArray(vao);
            GL.DrawElements(OpenTK.Graphics.OpenGL4.PrimitiveType.Triangles, slot.IndexCount, DrawElementsType.UnsignedInt, slot.IndexOffset * sizeof(uint));
            GL.BindVertexArray(0);
        }

        public void DrawAircraftDepth(ShadowMap shadowMap, Matrix4 aircraftModel, float propAngle, bool[] mountedMissiles, bool[] mountedBombs)
        {
            if (shadowMap == null) return;
            shadowMap.ApplyDepthModel(aircraftModel);
            GL.BindVertexArray(vao);
            GL.DrawElements(OpenTK.Graphics.OpenGL4.PrimitiveType.Triangles, airframeIndexCount, DrawElementsType.UnsignedInt, 0);
            for (int i = 0; i < missileSlots.Length && i < mountedMissiles.Length; i++)
                if (mountedMissiles[i] && missileSlots[i].IndexCount > 0)
                    GL.DrawElements(OpenTK.Graphics.OpenGL4.PrimitiveType.Triangles, missileSlots[i].IndexCount, DrawElementsType.UnsignedInt, missileSlots[i].IndexOffset * sizeof(uint));
            for (int i = 0; i < bombSlots.Length && i < mountedBombs.Length; i++)
                if (mountedBombs[i] && bombSlots[i].IndexCount > 0)
                    GL.DrawElements(OpenTK.Graphics.OpenGL4.PrimitiveType.Triangles, bombSlots[i].IndexCount, DrawElementsType.UnsignedInt, bombSlots[i].IndexOffset * sizeof(uint));
            Matrix4 propMat = Matrix4.CreateTranslation(-PropellerPivot)
                * Matrix4.CreateRotationZ(propAngle)
                * Matrix4.CreateTranslation(PropellerPivot)
                * aircraftModel;
            shadowMap.ApplyDepthModel(propMat);
            GL.DrawElements(OpenTK.Graphics.OpenGL4.PrimitiveType.Triangles, propellerIndexCount, DrawElementsType.UnsignedInt, airframeIndexCount * sizeof(uint));
            GL.BindVertexArray(0);
        }

        public void DrawAircraft(Matrix4 aircraftModel, float propAngle, bool[] mountedMissiles, bool[] mountedBombs, Vector3 tint, Matrix4 view, Matrix4 proj, DirectionalLight sunLight, Vector3 viewPos, AmbientLight ambientLight, Material material, ShadowMap? shadowMap = null)
        {
            bool savedGold = material.VictoryGold;
            material.VictoryGold = false;
            BindPlaneShaderAndState(view, proj, sunLight, viewPos, ambientLight, material, shadowMap, tint);
            material.VictoryGold = savedGold;

            GL.UniformMatrix4(modelLocation, true, ref aircraftModel);
            GL.BindVertexArray(vao);
            GL.DrawElements(OpenTK.Graphics.OpenGL4.PrimitiveType.Triangles, airframeIndexCount, DrawElementsType.UnsignedInt, 0);
            for (int i = 0; i < missileSlots.Length && i < mountedMissiles.Length; i++)
                if (mountedMissiles[i] && missileSlots[i].IndexCount > 0)
                    GL.DrawElements(OpenTK.Graphics.OpenGL4.PrimitiveType.Triangles, missileSlots[i].IndexCount, DrawElementsType.UnsignedInt, missileSlots[i].IndexOffset * sizeof(uint));
            for (int i = 0; i < bombSlots.Length && i < mountedBombs.Length; i++)
                if (mountedBombs[i] && bombSlots[i].IndexCount > 0)
                    GL.DrawElements(OpenTK.Graphics.OpenGL4.PrimitiveType.Triangles, bombSlots[i].IndexCount, DrawElementsType.UnsignedInt, bombSlots[i].IndexOffset * sizeof(uint));
            Matrix4 propMat = Matrix4.CreateTranslation(-PropellerPivot)
                * Matrix4.CreateRotationZ(propAngle)
                * Matrix4.CreateTranslation(PropellerPivot)
                * aircraftModel;
            GL.UniformMatrix4(modelLocation, true, ref propMat);
            GL.DrawElements(OpenTK.Graphics.OpenGL4.PrimitiveType.Triangles, propellerIndexCount, DrawElementsType.UnsignedInt, airframeIndexCount * sizeof(uint));
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
            tintLocation = GL.GetUniformLocation(planeShaderProgram, "uTint");

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

            vertexdata = new List<VertexData>();
            indices = new List<uint>();
            var propellerIndices = new List<uint>();
            var airframeTris = new List<(uint A, uint B, uint C)>();
            foreach (var mesh in model.Meshes)
            {
                uint first = (uint)vertexdata.Count;
                bool isPropeller = mesh.Name.Contains("Propeller", StringComparison.OrdinalIgnoreCase);
                for (int i = 0; i < mesh.VertexCount; i++)
                {
                    var v = mesh.Vertices[i];
                    var uv = mesh.TextureCoordinateChannels[0][i];
                    var n = mesh.Normals[i];
                    vertexdata.Add(new VertexData(v.X, v.Y, v.Z, uv.X, uv.Y));
                    normals.Add(new Vector3(n.X, n.Y, n.Z));
                }
                foreach (var face in mesh.Faces)
                {
                    if (isPropeller)
                    {
                        foreach (int index in face.Indices) propellerIndices.Add(first + (uint)index);
                    }
                    else if (face.Indices.Count == 3)
                    {
                        airframeTris.Add((first + (uint)face.Indices[0], first + (uint)face.Indices[1], first + (uint)face.Indices[2]));
                    }
                    else
                    {
                        foreach (int index in face.Indices) indices.Add(first + (uint)index);
                    }
                }
            }

            // Separate the 6 underwing missile submodels and 2 underwing bomb submodels by
            // topological connected components (welded by quantized vertex position).
            PartitionOrdnanceSubmodels(airframeTris, propellerIndices);
            if (airframeIndexCount == 0 || propellerIndexCount == 0)
                throw new InvalidDataException("Plane model must contain Airframe and Propeller meshes.");

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

        private void PartitionOrdnanceSubmodels(List<(uint A, uint B, uint C)> airframeTris, List<uint> propellerIndices)
        {
            // Weld identical 3D positions so Assimp normal/UV splits stay topologically connected.
            var posToWeld = new Dictionary<(int, int, int), int>(vertexdata.Count);
            var vertToWeld = new int[vertexdata.Count];
            for (int i = 0; i < vertexdata.Count; i++)
            {
                var v = vertexdata[i];
                var key = ((int)MathF.Round(v.x * 1000f), (int)MathF.Round(v.y * 1000f), (int)MathF.Round(v.z * 1000f));
                if (!posToWeld.TryGetValue(key, out int id))
                {
                    id = posToWeld.Count;
                    posToWeld[key] = id;
                }
                vertToWeld[i] = id;
            }

            int weldCount = posToWeld.Count;
            var parent = new int[weldCount];
            for (int i = 0; i < weldCount; i++) parent[i] = i;

            int Find(int x)
            {
                while (parent[x] != x)
                {
                    parent[x] = parent[parent[x]];
                    x = parent[x];
                }
                return x;
            }

            foreach (var tri in airframeTris)
            {
                int ra = Find(vertToWeld[tri.A]);
                int rb = Find(vertToWeld[tri.B]);
                int rc = Find(vertToWeld[tri.C]);
                if (rb != ra) parent[rb] = ra;
                if (rc != ra) parent[rc] = ra;
            }

            // Compute AABB and face count per connected component.
            var compBounds = new Dictionary<int, (Vector3 Min, Vector3 Max, int Faces)>();
            foreach (var tri in airframeTris)
            {
                int root = Find(vertToWeld[tri.A]);
                if (!compBounds.TryGetValue(root, out var b))
                {
                    b = (new Vector3(float.PositiveInfinity), new Vector3(float.NegativeInfinity), 0);
                }
                void Include(uint vi)
                {
                    var vd = vertexdata[(int)vi];
                    var p = new Vector3(vd.x, vd.y, vd.z);
                    b.Min = Vector3.ComponentMin(b.Min, p);
                    b.Max = Vector3.ComponentMax(b.Max, p);
                }
                Include(tri.A);
                Include(tri.B);
                Include(tri.C);
                b.Faces++;
                compBounds[root] = b;
            }

            // Tight local-space bounding boxes for the 6 missiles and 2 bombs in plane.obj.
            (Vector3 Min, Vector3 Max)[] missileBoxes =
            {
                (new Vector3(-4.63f,  0.00f, -0.76f), new Vector3(-4.27f, 0.365f, 1.18f)), // Left Outer
                (new Vector3(-4.27f,  0.00f, -0.76f), new Vector3(-3.93f, 0.365f, 1.18f)), // Left Mid
                (new Vector3(-3.93f,  0.00f, -0.76f), new Vector3(-3.60f, 0.365f, 1.18f)), // Left Inner
                (new Vector3( 3.50f, -0.01f, -1.03f), new Vector3( 3.84f, 0.335f, 0.92f)), // Right Inner
                (new Vector3( 3.84f, -0.01f, -1.03f), new Vector3( 4.18f, 0.335f, 0.92f)), // Right Mid
                (new Vector3( 4.18f, -0.01f, -1.03f), new Vector3( 4.53f, 0.335f, 0.92f)), // Right Outer
            };

            (Vector3 Min, Vector3 Max)[] bombBoxes =
            {
                (new Vector3(-3.68f, -0.50f, -1.15f), new Vector3(-2.98f, 0.26f, 1.08f)), // Left Bomb
                (new Vector3( 2.86f, -0.52f, -1.28f), new Vector3( 3.56f, 0.24f, 0.95f)), // Right Bomb
            };

            static bool FitsInside(Vector3 min, Vector3 max, Vector3 boxMin, Vector3 boxMax) =>
                min.X >= boxMin.X && max.X <= boxMax.X &&
                min.Y >= boxMin.Y && max.Y <= boxMax.Y &&
                min.Z >= boxMin.Z && max.Z <= boxMax.Z;

            // Map each component root to: 0 = airframe, 1..6 = missile[k-1], 10..11 = bomb[k-10].
            var compBucket = new Dictionary<int, int>(compBounds.Count);
            foreach (var kv in compBounds)
            {
                int bucket = 0;
                var (min, max, faces) = kv.Value;
                if (faces >= 16)
                {
                    for (int bi = 0; bi < BombCount; bi++)
                    {
                        if (FitsInside(min, max, bombBoxes[bi].Min, bombBoxes[bi].Max))
                        {
                            bucket = 10 + bi;
                            break;
                        }
                    }
                }
                if (bucket == 0)
                {
                    for (int mi = 0; mi < MissileCount; mi++)
                    {
                        if (FitsInside(min, max, missileBoxes[mi].Min, missileBoxes[mi].Max))
                        {
                            bucket = 1 + mi;
                            break;
                        }
                    }
                }
                compBucket[kv.Key] = bucket;
            }

            var missileIndexLists = new List<uint>[MissileCount];
            var missileMins = new Vector3[MissileCount];
            var missileMaxs = new Vector3[MissileCount];
            for (int i = 0; i < MissileCount; i++)
            {
                missileIndexLists[i] = new List<uint>();
                missileMins[i] = new Vector3(float.PositiveInfinity);
                missileMaxs[i] = new Vector3(float.NegativeInfinity);
            }

            var bombIndexLists = new List<uint>[BombCount];
            var bombMins = new Vector3[BombCount];
            var bombMaxs = new Vector3[BombCount];
            for (int i = 0; i < BombCount; i++)
            {
                bombIndexLists[i] = new List<uint>();
                bombMins[i] = new Vector3(float.PositiveInfinity);
                bombMaxs[i] = new Vector3(float.NegativeInfinity);
            }

            void TrackBounds(ref Vector3 min, ref Vector3 max, uint vi)
            {
                var vd = vertexdata[(int)vi];
                var p = new Vector3(vd.x, vd.y, vd.z);
                min = Vector3.ComponentMin(min, p);
                max = Vector3.ComponentMax(max, p);
            }

            foreach (var tri in airframeTris)
            {
                int bucket = compBucket[Find(vertToWeld[tri.A])];
                if (bucket >= 1 && bucket <= MissileCount)
                {
                    int mi = bucket - 1;
                    missileIndexLists[mi].Add(tri.A);
                    missileIndexLists[mi].Add(tri.B);
                    missileIndexLists[mi].Add(tri.C);
                    TrackBounds(ref missileMins[mi], ref missileMaxs[mi], tri.A);
                    TrackBounds(ref missileMins[mi], ref missileMaxs[mi], tri.B);
                    TrackBounds(ref missileMins[mi], ref missileMaxs[mi], tri.C);
                }
                else if (bucket >= 10 && bucket < 10 + BombCount)
                {
                    int bi = bucket - 10;
                    bombIndexLists[bi].Add(tri.A);
                    bombIndexLists[bi].Add(tri.B);
                    bombIndexLists[bi].Add(tri.C);
                    TrackBounds(ref bombMins[bi], ref bombMaxs[bi], tri.A);
                    TrackBounds(ref bombMins[bi], ref bombMaxs[bi], tri.B);
                    TrackBounds(ref bombMins[bi], ref bombMaxs[bi], tri.C);
                }
                else
                {
                    indices.Add(tri.A);
                    indices.Add(tri.B);
                    indices.Add(tri.C);
                }
            }

            airframeIndexCount = indices.Count;
            propellerIndexCount = propellerIndices.Count;
            indices.AddRange(propellerIndices);

            for (int i = 0; i < MissileCount; i++)
            {
                int offset = indices.Count;
                indices.AddRange(missileIndexLists[i]);
                Vector3 center = missileIndexLists[i].Count > 0
                    ? (missileMins[i] + missileMaxs[i]) * 0.5f
                    : (missileBoxes[i].Min + missileBoxes[i].Max) * 0.5f;
                missileSlots[i] = new OrdnanceSlot
                {
                    IndexOffset = offset,
                    IndexCount = missileIndexLists[i].Count,
                    LocalCenter = center,
                    Mounted = true
                };
            }

            for (int i = 0; i < BombCount; i++)
            {
                int offset = indices.Count;
                indices.AddRange(bombIndexLists[i]);
                Vector3 center = bombIndexLists[i].Count > 0
                    ? (bombMins[i] + bombMaxs[i]) * 0.5f
                    : (bombBoxes[i].Min + bombBoxes[i].Max) * 0.5f;
                bombSlots[i] = new OrdnanceSlot
                {
                    IndexOffset = offset,
                    IndexCount = bombIndexLists[i].Count,
                    LocalCenter = center,
                    Mounted = true
                };
            }

            Console.WriteLine($"Extracted ordnance submodels: 6 missiles ({string.Join(",", missileIndexLists.Select(l => l.Count / 3))} tris), 2 bombs ({string.Join(",", bombIndexLists.Select(l => l.Count / 3))} tris).");
        }
    }
}