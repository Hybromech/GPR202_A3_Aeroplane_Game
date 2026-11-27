using System.Collections.Generic;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace GPR202_Assessment3
{
    public class DirectionalLight
    {
        public Vector3 Direction { get; set; }
        public Vector3 Ambient { get; set; }
        public Vector3 Diffuse { get; set; }
        public Vector3 Specular { get; set; }

        private readonly Dictionary<int, LightUniformLocations> _shaderCache = new Dictionary<int, LightUniformLocations>();

        private struct LightUniformLocations
        {
            public int Direction;
            public int Ambient;
            public int Diffuse;
            public int Specular;
            public int ViewPos;
        }
     
        public DirectionalLight(
            Vector3? direction = null,
            Vector3? ambient = null,
            Vector3? diffuse = null,
            Vector3? specular = null)
        {
            Direction = Vector3.Normalize(direction ?? ShadowSettings.MiddayDirection);
            Ambient = ambient ?? new Vector3(0.25f, 0.25f, 0.28f);
            Diffuse = diffuse ?? new Vector3(0.95f, 0.92f, 0.85f);
            Specular = specular ?? new Vector3(0.25f, 0.25f, 0.25f);
        }
      
        public void ApplyToShader(int shaderProgram, Vector3 viewPos, string uniformName = "dirLight")
        {
            GL.UseProgram(shaderProgram);

            if (!_shaderCache.TryGetValue(shaderProgram, out var locs))
            {
                locs = new LightUniformLocations
                {
                    Direction = GL.GetUniformLocation(shaderProgram, $"{uniformName}.direction"),
                    Ambient = GL.GetUniformLocation(shaderProgram, $"{uniformName}.ambient"),
                    Diffuse = GL.GetUniformLocation(shaderProgram, $"{uniformName}.diffuse"),
                    Specular = GL.GetUniformLocation(shaderProgram, $"{uniformName}.specular"),
                    ViewPos = GL.GetUniformLocation(shaderProgram, "viewPos")
                };
                _shaderCache[shaderProgram] = locs;
            }

            Vector3 normDir = Direction.LengthSquared > 0.0001f ? Vector3.Normalize(Direction) : -Vector3.UnitY;

            if (locs.Direction != -1) GL.Uniform3(locs.Direction, normDir);
            if (locs.Ambient != -1) GL.Uniform3(locs.Ambient, Ambient);
            if (locs.Diffuse != -1) GL.Uniform3(locs.Diffuse, Diffuse);
            if (locs.Specular != -1) GL.Uniform3(locs.Specular, Specular);
            if (locs.ViewPos != -1) GL.Uniform3(locs.ViewPos, viewPos);
        }
    }
}