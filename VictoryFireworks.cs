using OpenTK.Mathematics;
namespace GPR202_Assessment3;

public sealed class VictoryFireworks
{
    // Bounded pool: continuous celebration without accumulating particles or draw calls.
    public ParticleSystem[] Bursts { get; } = Enumerable.Range(0,24).Select(_=>new ParticleSystem()).ToArray();
    private static readonly Vector3[] Palette = { new(5,0.3f,0.2f), new(0.2f,2,5), new(5,3,0.2f), new(0.2f,5,1), new(4,0.3f,5), new(5,4,2) };
    public Vector3[] Colors { get; } = Enumerable.Range(0,24).Select(i=>Palette[i%Palette.Length]).ToArray();
    private int nextBurst, region;
    private float countdown, width, length;
    private bool active;
    private Func<float,float,float>? groundHeight;
    public void Start(float mapWidth,float mapLength,Func<float,float,float> heightAt)
    {
        Clear();width=Math.Max(2,mapWidth);length=Math.Max(2,mapLength);
        groundHeight=heightAt ?? throw new ArgumentNullException(nameof(heightAt));active=true;
    }
    public void Clear(){foreach(var burst in Bursts)burst.Clear();nextBurst=region=0;countdown=0;active=false;groundHeight=null;}
    public void Update(float dt)
    {
        foreach(var burst in Bursts)burst.Update(dt);
        if(!active)return;
        countdown-=dt;
        while(countdown<=0){
            // Visit every quarter-by-quarter map region, with jitter inside each cell.
            int cell=(region*5)%16;
            float x=((cell%4+0.1f+Random.Shared.NextSingle()*0.8f)/4f-0.5f)*(width-2);
            float z=((cell/4+0.1f+Random.Shared.NextSingle()*0.8f)/4f-0.5f)*(length-2);
            float y=groundHeight!(x,z)+100f+Random.Shared.NextSingle()*100f;
            Bursts[nextBurst].Firework(new Vector3(x,y,z),Vector3.Zero);
            nextBurst=(nextBurst+1)%Bursts.Length;region=(region+1)%16;
            countdown+=0.15f;
        }
    }
}
