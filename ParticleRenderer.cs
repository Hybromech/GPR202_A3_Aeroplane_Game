using System;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
namespace GPR202_Assessment3;

public sealed class ParticleRenderer : IDisposable
{
    private int program, vao, vbo;
    private readonly float[] data = new float[256*4];
    private int viewLocation, projectionLocation, heightLocation, tintLocation;
    public ParticleRenderer()
    {
        int vertex=0,fragment=0;
        try {
            vertex=Compile(ShaderType.VertexShader,@"#version 330 core
layout(location=0) in vec3 position;
layout(location=1) in float life;
uniform mat4 view; uniform mat4 projection; uniform float viewportHeight;
out float fade;
void main(){vec4 p=vec4(position,1)*view;gl_Position=p*projection;
gl_PointSize=clamp((1.0+life*3.0)*viewportHeight/max(1.0,-p.z),2.0,96.0);fade=life;}");
            fragment=Compile(ShaderType.FragmentShader,@"#version 330 core
in float fade;out vec4 color;uniform vec3 tint;
void main(){float radius=length(gl_PointCoord*2.0-1.0);if(radius>1.0)discard;
vec3 fire=mix(vec3(0.8,0.04,0.005),vec3(5.0,2.5,0.4),fade);
if(tint.x>=0.0)fire=tint*(0.5+fade);
color=vec4(fire,(1.0-smoothstep(0.1,1.0,radius))*fade);}");
            program=GL.CreateProgram();GL.AttachShader(program,vertex);GL.AttachShader(program,fragment);GL.LinkProgram(program);
            GL.GetProgram(program,GetProgramParameterName.LinkStatus,out int ok);if(ok==0)throw new InvalidOperationException(GL.GetProgramInfoLog(program));
            vao=GL.GenVertexArray();vbo=GL.GenBuffer();GL.BindVertexArray(vao);GL.BindBuffer(BufferTarget.ArrayBuffer,vbo);
            GL.BufferData(BufferTarget.ArrayBuffer,data.Length*sizeof(float),IntPtr.Zero,BufferUsageHint.StreamDraw);
            GL.EnableVertexAttribArray(0);GL.VertexAttribPointer(0,3,VertexAttribPointerType.Float,false,16,0);
            GL.EnableVertexAttribArray(1);GL.VertexAttribPointer(1,1,VertexAttribPointerType.Float,false,16,12);
            GL.BindVertexArray(0);
            viewLocation=GL.GetUniformLocation(program,"view");projectionLocation=GL.GetUniformLocation(program,"projection");heightLocation=GL.GetUniformLocation(program,"viewportHeight");tintLocation=GL.GetUniformLocation(program,"tint");
        } catch {Dispose();throw;} finally {if(vertex!=0)GL.DeleteShader(vertex);if(fragment!=0)GL.DeleteShader(fragment);}
    }
    private static int Compile(ShaderType type,string source){int s=GL.CreateShader(type);GL.ShaderSource(s,source);GL.CompileShader(s);GL.GetShader(s,ShaderParameter.CompileStatus,out int ok);if(ok!=0)return s;string error=GL.GetShaderInfoLog(s);GL.DeleteShader(s);throw new InvalidOperationException(error);}
    public void Draw(ParticleSystem particles,Matrix4 view,Matrix4 projection,int height, Vector3? tint = null)
    {
        if(particles.Count==0)return;
        for(int i=0;i<particles.Count;i++){var p=particles.Particles[i];data[i*4]=p.Position.X;data[i*4+1]=p.Position.Y;data[i*4+2]=p.Position.Z;data[i*4+3]=1-p.Age/p.Lifetime;}
        bool blend=GL.IsEnabled(EnableCap.Blend),point=GL.IsEnabled(EnableCap.ProgramPointSize);
        GL.GetBoolean(GetPName.DepthWritemask,out bool depthWrite);
        GL.GetInteger(GetPName.BlendSrcRgb,out int srcRgb);GL.GetInteger(GetPName.BlendDstRgb,out int dstRgb);
        GL.GetInteger(GetPName.BlendSrcAlpha,out int srcAlpha);GL.GetInteger(GetPName.BlendDstAlpha,out int dstAlpha);
        GL.Enable(EnableCap.Blend);GL.BlendFunc(BlendingFactor.SrcAlpha,BlendingFactor.One);GL.Enable(EnableCap.ProgramPointSize);GL.DepthMask(false);
        GL.UseProgram(program);GL.Uniform3(tintLocation,tint ?? new Vector3(-1f));GL.UniformMatrix4(viewLocation,true,ref view);GL.UniformMatrix4(projectionLocation,true,ref projection);GL.Uniform1(heightLocation,(float)height);
        GL.BindVertexArray(vao);GL.BindBuffer(BufferTarget.ArrayBuffer,vbo);GL.BufferSubData(BufferTarget.ArrayBuffer,IntPtr.Zero,particles.Count*16,data);GL.DrawArrays(PrimitiveType.Points,0,particles.Count);GL.BindVertexArray(0);
        GL.DepthMask(depthWrite);GL.BlendFuncSeparate((BlendingFactorSrc)srcRgb,(BlendingFactorDest)dstRgb,(BlendingFactorSrc)srcAlpha,(BlendingFactorDest)dstAlpha);
        if(!blend)GL.Disable(EnableCap.Blend);if(!point)GL.Disable(EnableCap.ProgramPointSize);
    }
    public void Dispose(){if(vbo!=0)GL.DeleteBuffer(vbo);if(vao!=0)GL.DeleteVertexArray(vao);if(program!=0)GL.DeleteProgram(program);vbo=vao=program=0;}
}
