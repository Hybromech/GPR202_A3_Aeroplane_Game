using GPR202_Assessment3;using OpenTK.Mathematics;
var p=new ParticleSystem();p.Explode(new Vector3(1,2,3));if(p.Count!=256)throw new Exception("Burst missing");p.Update(1f/120);if(p.Particles[0].Position==new Vector3(1,2,3))throw new Exception("Particles must move");for(int i=0;i<360;i++){p.Update(1f/120);for(int j=0;j<p.Count;j++)if(!float.IsFinite(p.Particles[j].Position.Y))throw new Exception("Invalid particle");}if(p.Count!=0)throw new Exception("Particles outlive restart");p.Explode(Vector3.Zero);p.Clear();if(p.Count!=0)throw new Exception("Restart must clear particles");Console.WriteLine("PASS burst, motion, expiration and reset.");
var fireworks=new VictoryFireworks();int launches=0;var regions=new HashSet<(int,int)>();
float Ground(float x,float z){if(Math.Abs(x)>1024||Math.Abs(z)>1024)throw new Exception("Outside map");regions.Add(((int)((x+1024)/512),(int)((z+1024)/512)));launches++;return 123;}
fireworks.Start(2048,2048,Ground);fireworks.Update(0.01f);if(launches!=1)throw new Exception("Immediate first burst");
for(int i=0;i<7200;i++)fireworks.Update(1f/120);
if(launches<390||regions.Count!=16)throw new Exception("Must continue across all map regions");
if(fireworks.Bursts.Length!=24||fireworks.Bursts.Sum(b=>b.Count)>6144)throw new Exception("Pool must stay bounded");
if(!fireworks.Bursts.Any(b=>b.Count>0))throw new Exception("Celebration stopped");
fireworks.Clear();int previous=launches;for(int i=0;i<600;i++)fireworks.Update(1f/120);if(launches!=previous||fireworks.Bursts.Any(b=>b.Count>0))throw new Exception("Reset must stop fireworks");
Console.WriteLine("PASS continuous 60-second fireworks, all 16 map regions, bounded pool and reset.");
