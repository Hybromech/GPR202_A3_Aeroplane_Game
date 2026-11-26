using GPR202_Assessment3;
using System.Diagnostics;
static void Assert(bool b,string message){if(!b)throw new Exception(message);}
static double Energy(float[] b)=>b.Sum(x=>(double)x*x)/b.Length;
var block=new float[2048];
foreach(var layer in new[]{"engine","music","pop"}){
var settings=new AudioSettings{EngineVolume=layer=="engine"?1:0,MusicVolume=layer=="music"?1:0,PopVolume=layer=="pop"?1:0};
var mix=new GameAudioMix(settings);if(layer=="pop")mix.PlayBalloonPop();
double peakEnergy=0;for(int j=0;j<100;j++){mix.Read(block,0,block.Length);Assert(block.All(x=>float.IsFinite(x)&&Math.Abs(x)<=1),"Finite bounded samples");peakEnergy=Math.Max(peakEnergy,Energy(block));}
Assert(peakEnergy>0.00001,layer+" must produce sound");if(layer=="pop")Assert(Energy(block)==0,"Pop must end");
mix.Focused=false;for(int j=0;j<30;j++)mix.Read(block,0,block.Length);Assert(Energy(block)==0,"Unfocused audio must fade silent");mix.Focused=true;mix.PlayBalloonPop();mix.Read(block,0,block.Length);Assert(Energy(block)>0,"Audio must resume");
}
var warning=new GameAudioMix(new AudioSettings{EngineVolume=0,MusicVolume=0,PopVolume=0});warning.StallWarning=1;warning.Read(block,0,block.Length);Assert(Energy(block)>0.00001,"Stall warning must produce sound");warning.StallWarning=0;for(int j=0;j<30;j++)warning.Read(block,0,block.Length);Assert(Energy(block)<0.00000001,"Stall warning must fade after recovery");
var blast=new GameAudioMix(new AudioSettings{EngineVolume=0,MusicVolume=0,PopVolume=0});blast.PlayExplosion();blast.Read(block,0,block.Length);Assert(Energy(block)>0.00001,"Explosion must produce sound");for(int j=0;j<130;j++)blast.Read(block,0,block.Length);Assert(Energy(block)==0,"Explosion must end");
var stopped=new GameAudioMix(new AudioSettings{MusicVolume=0,PopVolume=0});stopped.EngineRunning=false;for(int j=0;j<30;j++)stopped.Read(block,0,block.Length);Assert(Energy(block)<0.00000001,"Destroyed engine must become silent");
var stress=new GameAudioMix(new AudioSettings());var sw=Stopwatch.StartNew();for(int j=0;j<1500;j++){if(j%10==0)for(int n=0;n<8;n++)stress.PlayBalloonPop();stress.Throttle=(j%100)/100f;stress.Read(block,0,block.Length);Assert(block.All(x=>float.IsFinite(x)&&Math.Abs(x)<=1),"Mixed samples bounded");}Console.WriteLine($"PASS audio layers, pop completion, focus fade/resume and overlapping voices; rendered 34.8 seconds in {sw.Elapsed.TotalSeconds:F2}s.");
using(var device=new GameAudio(new AudioSettings())) {device.Update(0.5f,true);device.PlayBalloonPop();Thread.Sleep(200);}
Console.WriteLine("PASS audio device initialization/fallback and disposal.");
