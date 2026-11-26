using System;using GPR202_Assessment3;using OpenTK.Mathematics;class Check {static void Assert(bool b,string m){if(!b)throw new Exception(m);}static void CheckRoll(){
foreach(float input in new[]{-1f,1f}) {
var f=new FlightPhysics(new FlightSettings{AssistedControls=false}); f.Reset(new Vector3(0,1000,0)); const float dt=1f/120;
float Increment(float control) {var right=f.Right;var up=f.Up;f.Step(dt,0,control,0);return input*MathF.Atan2(Vector3.Dot(f.Right,up),Vector3.Dot(f.Right,right));}
float first=Increment(input),second=Increment(input);
Assert(first>0 && second>first,"Held input must accelerate roll in the requested direction");
float released=Increment(0);
Assert(released>0 && released<second,"Centred input must damp existing roll, not stop instantly");
} Console.WriteLine("PASS: roll direction, acceleration and damping.");
} static void CheckBankLimit(){
foreach(float sign in new[]{-1f,1f}) {
var f=new FlightPhysics(new FlightSettings{AssistedControls=false});f.Reset(new Vector3(0,1000,0)); const float dt=1f/120; bool reached=false;
for(int i=0;i<600;i++) {f.Step(dt,0,sign,0);float bank=MathF.Atan2(f.Right.Y,f.Up.Y);if(f.Right.Y*f.Right.Y+f.Up.Y*f.Up.Y>0.0001f)Assert(MathF.Abs(bank)<=MathF.PI/2+0.0001f,"Bank must stay within 90 degrees");reached|=sign*bank>1.56f;}
Assert(reached,"Must reach requested bank limit");
for(int i=0;i<60;i++)f.Step(dt,0,-sign,0);
Assert(sign*MathF.Atan2(f.Right.Y,f.Up.Y)<1.5f,"Reverse input must leave bank limit");
}Console.WriteLine("PASS: left/right 90-degree bank limits and reversal.");
} static void CheckAssisted(){
const float dt=1f/120;
foreach(float sign in new[]{-1f,1f}) {
var f=new FlightPhysics(new FlightSettings());f.Reset(new Vector3(0,1000,0));
for(int n=0;n<360;n++){f.Step(dt,0,sign*0.5f,0);Assert(Math.Abs(f.BankAngle)<=MathF.PI/3+0.001,"Assisted bank cap");}
Console.WriteLine($"Assisted target 30deg: {MathHelper.RadiansToDegrees(f.BankAngle)}");
Assert(Math.Abs(f.BankAngle-sign*MathF.PI/6)<0.12,"Assisted bank must track target");
for(int n=0;n<360;n++)f.Step(dt,0,0,0);
Assert(Math.Abs(f.BankAngle)<0.05,"Centre must level wings");
}
var cfg=new FlightSettings();var p=new FlightPhysics(cfg);p.Reset(new Vector3(0,1000,0));bool stalled=false,warned=false;
for(int n=0;n<2400;n++){p.Step(dt,1,0,0);stalled|=p.IsStalled;warned|=p.StallWarning>0;}
Assert(stalled&&warned,"Assisted hard pull must still warn and stall");p.Throttle=1f;bool recovered=false;
for(int n=0;n<2400;n++){p.Step(dt,-0.25f,0,0);recovered|=!p.IsStalled&&p.Airspeed>30;}
Assert(recovered,"Assisted stall must be recoverable");
for(int n=0;n<72000;n++){if(n%1200==0)cfg.AssistedControls=!cfg.AssistedControls;p.Step(dt,MathF.Sin(n*.003f),MathF.Sin(n*.002f),0.1f);Assert(float.IsFinite(p.Position.X)&&float.IsFinite(p.Position.Y)&&float.IsFinite(p.Position.Z),"Mode switching must remain finite");Assert(Math.Abs(p.Forward.Length-1)<.001&&Math.Abs(Vector3.Dot(p.Forward,p.Up))<.001,"Assisted basis orthonormal");}
Console.WriteLine("PASS assisted bank tracking, levelling, stalls, recovery, warning and 10-minute mode-switch stability.");
} static void CheckThrottle(){
var low=new FlightPhysics(new FlightSettings());var high=new FlightPhysics(new FlightSettings());low.Reset(new Vector3(0,1000,0));high.Reset(new Vector3(0,1000,0));low.Throttle=0;high.Throttle=1;
for(int n=0;n<600;n++){low.Step(1f/120,0,0,0);high.Step(1f/120,0,0,0);}
Console.WriteLine($"Throttle comparison after 5s: idle {low.Airspeed:F1} m/s; full {high.Airspeed:F1} m/s; prop fractions {low.PropellerSpeedFraction:F2}/{high.PropellerSpeedFraction:F2}");
Assert(high.Airspeed-low.Airspeed>12,"Throttle must produce a noticeable speed difference");Assert(low.PropellerSpeedFraction<0.21&&high.PropellerSpeedFraction>0.99,"Propeller must follow engine power");
high.Throttle=0;float before=high.EnginePower;high.Step(1f/120,0,0,0);Assert(high.EnginePower<before&&high.EnginePower>0.9,"Engine must spool down smoothly");
} static void Main(){CheckThrottle();CheckAssisted();CheckBankLimit();const float dt=1f/120; CheckRoll();var cfg=new FlightSettings{AssistedControls=false};var p=new FlightPhysics(cfg);p.Reset(new Vector3(0,1000,0));Assert(p.LiftCoefficient(MathHelper.DegreesToRadians(35))<p.LiftCoefficient(MathHelper.DegreesToRadians(16)),"Lift must drop beyond stall");bool stalled=false;float maxAoA=0;for(int i=0;i<1200;i++){p.Step(dt,1,0,0);stalled|=p.IsStalled;maxAoA=Math.Max(maxAoA,Math.Abs(p.AngleOfAttack));}Console.WriteLine($"Pull-up max AoA={MathHelper.RadiansToDegrees(maxAoA)}, stalled={stalled}, speed={p.Airspeed}");Assert(stalled,"Hard pull must stall");p.Throttle=1f;bool recovered=false;for(int i=0;i<2400;i++){p.Step(dt,-0.25f,0,0);if(!p.IsStalled&&p.Airspeed>30)recovered=true;}Assert(recovered,"Must recover with reduced AoA");p.Reset(new Vector3(0,1000,0));p.Throttle=0;for(int i=0;i<1200;i++)p.Step(dt,0,0,0);Assert(p.Position.Y<1000,"Engine-off flight must descend");for(int i=0;i<72000;i++){p.Step(dt,MathF.Sin(i*.003f),MathF.Sin(i*.002f),.1f);Assert(float.IsFinite(p.Position.X)&&float.IsFinite(p.Position.Y)&&float.IsFinite(p.Position.Z),"Finite state");Assert(Math.Abs(p.Forward.Length-1)<.001&&Math.Abs(Vector3.Dot(p.Forward,p.Up))<.001,"Orthonormal orientation");}Console.WriteLine("PASS: lift drop, accelerated stall, recovery, engine-off descent and 10-minute stability.");}}