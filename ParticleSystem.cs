using System;
using OpenTK.Mathematics;
namespace GPR202_Assessment3;

// Fixed-capacity CPU simulation; the renderer uploads only live particles.
public sealed class ParticleSystem
{
    public struct Particle { public Vector3 Position, Velocity; public float Age, Lifetime; }
    public Particle[] Particles { get; } = new Particle[256];
    public int Count { get; private set; }
    public void Clear() => Count = 0;
    public void Explode(Vector3 position)
    {
        Count = Particles.Length;
        for(int i=0;i<Count;i++) {
            Vector3 direction=new(Random.Shared.NextSingle()*2-1,Random.Shared.NextSingle()*1.5f,Random.Shared.NextSingle()*2-1);
            if(direction.LengthSquared<0.001f) direction=Vector3.UnitY;
            direction.Normalize();
            Particles[i]=new Particle{Position=position,Velocity=direction*(8+Random.Shared.NextSingle()*25),Lifetime=0.6f+Random.Shared.NextSingle()*1.9f};
        }
    }
    public void Update(float dt)
    {
        for(int i=Count-1;i>=0;i--) {
            ref Particle p=ref Particles[i];p.Age+=dt;
            if(p.Age>=p.Lifetime){Particles[i]=Particles[--Count];continue;}
            p.Velocity*=MathF.Exp(-1.2f*dt);p.Velocity.Y-=9.81f*dt;p.Position+=p.Velocity*dt;
        }
    }
}
