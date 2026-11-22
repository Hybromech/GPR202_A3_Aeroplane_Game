using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace GPR202_Assessment3
{
    // Uniform scene fill light; independent of the sun and shadow map.
    public sealed class AmbientLight
    {
        private readonly ShadowSettings settings;
        private readonly Dictionary<int, int> locations = new();

        public AmbientLight(ShadowSettings settings)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public Vector3 Radiance => settings.AmbientEnabled
            ? Vector3.ComponentMax(settings.AmbientColor, Vector3.Zero) * Math.Max(0f, settings.AmbientIntensity)
            : Vector3.Zero;

        public void ApplyToShader(int shaderProgram)
        {
            GL.UseProgram(shaderProgram);
            if (!locations.TryGetValue(shaderProgram, out int location))
            {
                location = GL.GetUniformLocation(shaderProgram, "ambientLight");
                locations.Add(shaderProgram, location);
            }
            if (location != -1) GL.Uniform3(location, Radiance);
        }
    }
}
