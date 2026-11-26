using OpenTK.Mathematics;
namespace GPR202_Assessment3;

public sealed class VictoryFireworks
{
    public ParticleSystem[] Bursts { get; } = Enumerable.Range(0,6).Select(_=>new ParticleSystem()).ToArray();
    public Vector3[] Colors { get; } = { new(5,0.3f,0.2f), new(0.2f,2,5), new(5,3,0.2f), new(0.2f,5,1), new(4,0.3f,5), new(5,4,2) };
    private int launched;
    private float elapsed;
    private bool active;
    public void Start(){Clear();active=true;}
    public void Clear(){foreach(var burst in Bursts)burst.Clear();launched=0;elapsed=0;active=false;}
    public void Update(float dt,Vector3 position,Vector3 forward,Vector3 velocity)
    {
        foreach(var burst in Bursts)burst.Update(dt);
        if(!active)return;
        elapsed+=dt;
        while(launched<Bursts.Length && elapsed>=launched*0.5f){
            Vector3 side=Vector3.Cross(forward,Vector3.UnitY);
            if(side.LengthSquared<0.001f)side=Vector3.UnitX;else side.Normalize();
            float offset=(launched%2==0?-1:1)*(14+launched*2);
            Bursts[launched].Firework(position+forward*65+side*offset+Vector3.UnitY*(18+launched%3*8),velocity*0.7f);
            launched++;
        }
        if(launched==Bursts.Length)active=false;
    }
}
