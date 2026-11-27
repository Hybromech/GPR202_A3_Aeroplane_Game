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
float maxBankRad=MathHelper.DegreesToRadians(new FlightSettings().AssistedMaxBankDegrees);
for(int n=0;n<360;n++){f.Step(dt,0,sign*0.5f,0);Assert(Math.Abs(f.BankAngle)<=maxBankRad+0.02f,"Assisted bank cap");}
Console.WriteLine($"Assisted target {MathHelper.RadiansToDegrees(sign*0.5f*maxBankRad):F1}deg: {MathHelper.RadiansToDegrees(f.BankAngle):F1}deg, Forward.X={f.Forward.X:F2}");
Assert(Math.Abs(f.BankAngle-sign*0.5f*maxBankRad)<0.14,"Assisted bank must track target");
Assert(sign*f.Forward.X>0.4f,"Assisted left/right input must turn heading convincingly in requested direction");
for(int n=0;n<360;n++)f.Step(dt,0,0,0);
Assert(Math.Abs(f.BankAngle)<0.06,"Centre must level wings");
}
// Verify horizon-relative climb while banked: mouse up (+pitch) climbs relative to ground even during a turn.
{
var bankedClimb=new FlightPhysics(new FlightSettings());bankedClimb.Reset(new Vector3(0,1000,0));
for(int n=0;n<120;n++)bankedClimb.Step(dt,0,1f,0);
float yBefore=bankedClimb.Position.Y;
for(int n=0;n<240;n++)bankedClimb.Step(dt,0.7f,0.8f,0);
Console.WriteLine($"BankedClimb: dY={bankedClimb.Position.Y-yBefore:F1}, FwdY={bankedClimb.Forward.Y:F2}, Bank={MathHelper.RadiansToDegrees(bankedClimb.BankAngle):F1}deg, AoA={MathHelper.RadiansToDegrees(bankedClimb.AngleOfAttack):F1}deg, Spd={bankedClimb.Airspeed:F1}");
Assert(bankedClimb.Position.Y>yBefore+20f && bankedClimb.Forward.Y>0.1f,"Assisted pitch-up while banked must climb relative to ground");
}
// Verify extreme screen edge pulls up heavily for a much sharper turn than mid-screen input.
{
float MeasureTurnDeg(float rollCmd) {
var t=new FlightPhysics(new FlightSettings());t.Reset(new Vector3(0,1000,0));
Vector3 startFwd=new Vector3(t.Forward.X,0,t.Forward.Z).Normalized();
float maxAoA=0f;
for(int n=0;n<240;n++){t.Step(dt,0,rollCmd,0);maxAoA=Math.Max(maxAoA,MathHelper.RadiansToDegrees(t.AngleOfAttack));}
Vector3 endFwd=new Vector3(t.Forward.X,0,t.Forward.Z).Normalized();
float deg=MathHelper.RadiansToDegrees(MathF.Acos(Math.Clamp(Vector3.Dot(startFwd,endFwd),-1f,1f)));
Console.WriteLine($"Turn roll={rollCmd:F1}: headingTurn={deg:F1}deg, bank={MathHelper.RadiansToDegrees(t.BankAngle):F1}deg, maxAoA={maxAoA:F1}deg, dY={t.Position.Y-1000f:F1}, spd={t.Airspeed:F1}");
return deg;
}
float midTurn=MeasureTurnDeg(0.5f);
float edgeTurn=MeasureTurnDeg(1.0f);
Assert(edgeTurn>midTurn+30f,"Extreme screen edge must carve a noticeably sharper turn than mid-screen");
}
// Verify separate gamepad controls: direct roll (holds bank), pitch, starboard/port strafe, and right-stick-click auto-level.
{
var gp=new FlightPhysics(new FlightSettings());gp.Reset(new Vector3(0,1000,0));
// Roll right with Left Stick X, then release and verify bank is held (does not snap back to 0).
for(int n=0;n<60;n++)gp.StepGamepad(dt,0f,0.8f,0f,false);
float bankAfterRoll=gp.BankAngle;
for(int n=0;n<60;n++)gp.StepGamepad(dt,0f,0f,0f,false);
Console.WriteLine($"Gamepad roll: after={MathHelper.RadiansToDegrees(bankAfterRoll):F1}deg, held={MathHelper.RadiansToDegrees(gp.BankAngle):F1}deg");
Assert(bankAfterRoll>0.35f && gp.BankAngle>0.30f,"Gamepad Left Stick X must roll right and hold bank on release");
// Pitch up with stick Y, then trigger Right Stick Click leveling and verify bank & pitch level out.
for(int n=0;n<40;n++)gp.StepGamepad(dt,0.7f,0f,0f,false);
for(int n=0;n<240;n++)gp.StepGamepad(dt,0f,0f,0f,true);
Console.WriteLine($"Gamepad level: bank={MathHelper.RadiansToDegrees(gp.BankAngle):F1}deg, FwdY={gp.Forward.Y:F3}, approxLevel={gp.IsApproximatelyLevel}");
Assert(MathF.Abs(gp.BankAngle)<0.05f && MathF.Abs(gp.Forward.Y)<0.05f,"Gamepad Right Stick Click must level both bank and pitch");
// Strafe right (+1) while facing -Z must move plane toward +X (starboard).
gp.Reset(new Vector3(0,1000,0));
float xBefore=gp.Position.X;
for(int n=0;n<90;n++)gp.StepGamepad(dt,0f,0f,1f,false);
float dxRight=gp.Position.X-xBefore;
gp.Reset(new Vector3(0,1000,0));
for(int n=0;n<90;n++)gp.StepGamepad(dt,0f,0f,-1f,false);
float dxLeft=gp.Position.X;
Console.WriteLine($"Gamepad strafe: right dX={dxRight:F1}m, left dX={dxLeft:F1}m");
Assert(dxRight>6f && dxLeft<-6f,"Gamepad Right Stick X must strafe starboard (+X) and port (-X)");
// Dual-stick combined pitch must pitch much more aggressively than single-stick pitch.
float singleCmd=1f;
float dualCmd=2.6f;
float singleDiveCmd=-1f;
float dualDiveCmd=-2.6f;
var gpSingle=new FlightPhysics(new FlightSettings());gpSingle.Reset(new Vector3(0,1000,0));
var gpDual=new FlightPhysics(new FlightSettings());gpDual.Reset(new Vector3(0,1000,0));
for(int n=0;n<60;n++){gpSingle.StepGamepad(dt,singleCmd,0f,0f,false);gpDual.StepGamepad(dt,dualCmd,0f,0f,false);}
float singlePitchDeg=MathHelper.RadiansToDegrees(MathF.Asin(Math.Clamp(gpSingle.Forward.Y,-1f,1f)));
float dualPitchDeg=MathHelper.RadiansToDegrees(MathF.Asin(Math.Clamp(gpDual.Forward.Y,-1f,1f)));
gpSingle.Reset(new Vector3(0,1000,0));gpDual.Reset(new Vector3(0,1000,0));
for(int n=0;n<60;n++){gpSingle.StepGamepad(dt,singleDiveCmd,0f,0f,false);gpDual.StepGamepad(dt,dualDiveCmd,0f,0f,false);}
float singleDiveDeg=MathHelper.RadiansToDegrees(MathF.Asin(Math.Clamp(gpSingle.Forward.Y,-1f,1f)));
float dualDiveDeg=MathHelper.RadiansToDegrees(MathF.Asin(Math.Clamp(gpDual.Forward.Y,-1f,1f)));
Console.WriteLine($"Gamepad pitch (0.5s): singleUp={singlePitchDeg:F1}deg vs dualUp={dualPitchDeg:F1}deg; singleDown={singleDiveDeg:F1}deg vs dualDown={dualDiveDeg:F1}deg");
Assert(dualCmd>singleCmd*2f && dualPitchDeg>singlePitchDeg*1.7f && dualDiveDeg<singleDiveDeg*1.7f,"Both sticks combined must produce much stronger pitch up and down");
// Verify natural bank-induced turn: bank right (~45deg) then release sticks -> plane naturally turns right.
foreach(float sign in new[]{-1f,1f}) {
var bt=new FlightPhysics(new FlightSettings());bt.Reset(new Vector3(0,1000,0));
for(int n=0;n<60;n++)bt.StepGamepad(dt,0f,sign*0.9f,0f,false);
float bankHeld=MathHelper.RadiansToDegrees(bt.BankAngle);
for(int n=0;n<240;n++)bt.StepGamepad(dt,0f,0f,0f,false);
Vector3 endFlat=new Vector3(bt.Forward.X,0,bt.Forward.Z).Normalized();
float turnDeg=MathHelper.RadiansToDegrees(MathF.Acos(Math.Clamp(Vector3.Dot(new Vector3(0,0,-1),endFlat),-1f,1f)));
Console.WriteLine($"Gamepad natural bank turn ({(sign>0?"right":"left")}): bank={bankHeld:F1}deg -> turned={turnDeg:F1}deg, FwdX={bt.Forward.X:F2}, dY={bt.Position.Y-1000f:F1}");
Assert(sign*bt.Forward.X>0.35f && turnDeg>20f,"Banking with sticks released must naturally turn the plane in the bank direction");
}
// Verify EnableMaxBankAngle=false allows rolling past 90 deg (inverted) and R3 levels back upright.
{
var freeRoll=new FlightPhysics(new FlightSettings{EnableMaxBankAngle=false});freeRoll.Reset(new Vector3(0,1000,0));
bool wentInverted=false;
for(int n=0;n<120;n++){freeRoll.StepGamepad(dt,0f,1f,0f,false);wentInverted|=MathF.Abs(freeRoll.BankAngle)>MathHelper.DegreesToRadians(110f);}
Assert(wentInverted,"EnableMaxBankAngle=false must allow rolling past 90 degrees");
for(int n=0;n<240;n++)freeRoll.StepGamepad(dt,0f,0f,0f,true);
Assert(MathF.Abs(freeRoll.BankAngle)<0.05f,"R3 leveling must recover upright even from inverted roll");
}
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