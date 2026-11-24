using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL4;
namespace GPR202_Assessment3
{
    // Metallic workflow: 0 is dielectric, 1 is metal; roughness sets highlight width.
    public sealed class Material
    {
        private readonly ShadowSettings settings;
        private readonly Dictionary<int, (int Metalness, int Roughness)> locations = new();
        public Material(ShadowSettings settings) => this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        public float Metalness { get => Math.Clamp(settings.PlaneMetalness, 0f, 1f); set => settings.PlaneMetalness = Math.Clamp(value, 0f, 1f); }
        public float Roughness { get => Math.Clamp(settings.PlaneRoughness, 0.05f, 1f); set => settings.PlaneRoughness = Math.Clamp(value, 0.05f, 1f); }
        public void ApplyToShader(int program)
        {
            if (!locations.TryGetValue(program, out var loc))
            {
                loc = (GL.GetUniformLocation(program, "uMetalness"), GL.GetUniformLocation(program, "uRoughness"));
                locations.Add(program, loc);
            }
            GL.UseProgram(program);
            GL.Uniform1(loc.Metalness, Metalness);
            GL.Uniform1(loc.Roughness, Roughness);
        }
    }
}
