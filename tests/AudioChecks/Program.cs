using System.Diagnostics;
using GPR202_Assessment3;

static void Assert(bool b, string message)
{
    if (!b) throw new Exception(message);
}

static double Energy(float[] b) => b.Sum(x => (double)x * x) / b.Length;

var block = new float[2048];

// Verify all expected Resource/Audio WAV files are discovered and loaded.
using (var checkAssets = new GameAudioMix(new AudioSettings()))
{
    Assert(checkAssets.HasEngineClip, "effects/engine.wav must load");
    Assert(checkAssets.HasExplosionClip, "effects/explode.wav must load");
    Assert(checkAssets.HasWarningClip, "effects/warning_beep.wav must load");
    Assert(checkAssets.HasMusicTrack(MusicTrack.InGame), "music/background_in_game.wav must be available");
    Assert(checkAssets.HasMusicTrack(MusicTrack.Victory), "music/win.wav must be available");
    Assert(checkAssets.HasMusicTrack(MusicTrack.Death), "music/death.wav must be available");
    Assert(checkAssets.HasMusicTrack(MusicTrack.Menu), "music/menu.wav must be available");
}

// Verify engine, music, and balloon pop layers independently, plus focus fade/resume.
foreach (var layer in new[] { "engine", "music", "pop" })
{
    var settings = new AudioSettings
    {
        EngineVolume = layer == "engine" ? 1f : 0f,
        MusicVolume = layer == "music" ? 1f : 0f,
        PopVolume = layer == "pop" ? 1f : 0f
    };
    using var mix = new GameAudioMix(settings);
    if (layer == "pop") mix.PlayBalloonPop();

    double peakEnergy = 0;
    int blocks = layer == "music" ? 150 : 100;
    for (int j = 0; j < blocks; j++)
    {
        mix.Read(block, 0, block.Length);
        Assert(block.All(x => float.IsFinite(x) && Math.Abs(x) <= 1f), "Finite bounded samples");
        peakEnergy = Math.Max(peakEnergy, Energy(block));
    }
    Assert(peakEnergy > 0.00001, layer + " must produce sound");
    if (layer == "pop") Assert(Energy(block) == 0, "Pop must end");

    mix.Focused = false;
    for (int j = 0; j < 35; j++) mix.Read(block, 0, block.Length);
    Assert(Energy(block) == 0, "Unfocused audio must fade silent");

    mix.Focused = true;
    if (layer == "pop") mix.PlayBalloonPop();
    double resumeEnergy = 0;
    for (int j = 0; j < 10; j++)
    {
        mix.Read(block, 0, block.Length);
        resumeEnergy = Math.Max(resumeEnergy, Energy(block));
    }
    Assert(resumeEnergy > 0, layer + " audio must resume on focus");
}

// Verify stall warning beep plays and fades silent on recovery.
using (var warning = new GameAudioMix(new AudioSettings { EngineVolume = 0, MusicVolume = 0, PopVolume = 0, StallWarningVolume = 1f }))
{
    warning.StallWarning = 1f;
    warning.Read(block, 0, block.Length);
    Assert(Energy(block) > 0.00001, "Stall warning beep must produce sound");
    warning.StallWarning = 0f;
    for (int j = 0; j < 40; j++) warning.Read(block, 0, block.Length);
    Assert(Energy(block) < 0.00000001, "Stall warning must fade after recovery");
}

// Verify explosion WAV plays and ends cleanly after its duration (~3.87s).
using (var blast = new GameAudioMix(new AudioSettings { EngineVolume = 0, MusicVolume = 0, PopVolume = 0, ExplosionVolume = 1f }))
{
    blast.PlayExplosion();
    blast.Read(block, 0, block.Length);
    Assert(Energy(block) > 0.00001, "Explosion must produce sound");
    for (int j = 0; j < 185; j++) blast.Read(block, 0, block.Length);
    Assert(Energy(block) == 0, "Explosion must end");
}

// Verify engine fades silent when crashed.
using (var stopped = new GameAudioMix(new AudioSettings { MusicVolume = 0, PopVolume = 0, EngineVolume = 1f }))
{
    stopped.EngineRunning = false;
    for (int j = 0; j < 35; j++) stopped.Read(block, 0, block.Length);
    Assert(Energy(block) < 0.00000001, "Destroyed engine must become silent");
}

// Verify all music tracks (InGame, Victory, Death, Menu) produce distinct, finite audio and crossfade cleanly.
static double MeasureTrack(MusicTrack track, out float[] sampleTail)
{
    var buf = new float[2048];
    using var m = new GameAudioMix(new AudioSettings { EngineVolume = 0, PopVolume = 0, MusicVolume = 1f });
    m.MusicTrack = track;
    double peak = 0;
    for (int j = 0; j < 180; j++)
    {
        m.Read(buf, 0, buf.Length);
        Assert(buf.All(x => float.IsFinite(x) && Math.Abs(x) <= 1f), track + " music must be finite and bounded");
        peak = Math.Max(peak, Energy(buf));
    }
    sampleTail = buf;
    return peak;
}

double inGamePeak = MeasureTrack(MusicTrack.InGame, out var inGameTail);
double victoryPeak = MeasureTrack(MusicTrack.Victory, out var victoryTail);
double deathPeak = MeasureTrack(MusicTrack.Death, out var deathTail);
double menuPeak = MeasureTrack(MusicTrack.Menu, out var menuTail);

Assert(inGamePeak > 0.00001, "InGame music must produce sound");
Assert(victoryPeak > 0.00001, "Victory music must produce sound");
Assert(deathPeak > 0.00001, "Death music must produce sound");
Assert(menuPeak > 0.00001, "Menu music must produce sound");
Assert(inGameTail.Zip(victoryTail, (a, b) => Math.Abs(a - b)).Sum() > 1, "Victory music must differ from InGame");
Assert(inGameTail.Zip(deathTail, (a, b) => Math.Abs(a - b)).Sum() > 1, "Death music must differ from InGame");

// Verify crossfading Victory -> InGame and Death -> InGame stays smooth and active.
using (var transitions = new GameAudioMix(new AudioSettings { EngineVolume = 0, PopVolume = 0, MusicVolume = 1f }))
{
    transitions.Victory = true;
    for (int j = 0; j < 120; j++) transitions.Read(block, 0, block.Length);
    Assert(transitions.MusicTrack == MusicTrack.Victory, "Victory setter selects Victory track");
    transitions.MusicTrack = MusicTrack.Death;
    for (int j = 0; j < 120; j++) transitions.Read(block, 0, block.Length);
    transitions.MusicTrack = MusicTrack.InGame;
    double restoredPeak = 0;
    for (int j = 0; j < 120; j++)
    {
        transitions.Read(block, 0, block.Length);
        Assert(block.All(x => float.IsFinite(x) && Math.Abs(x) <= 1f), "Crossfaded samples must be finite and bounded");
        restoredPeak = Math.Max(restoredPeak, Energy(block));
    }
    Assert(restoredPeak > 0.00001, "Restored InGame music must produce sound");
}

// Verify graceful fallback when audio directory is missing.
using (var missing = new GameAudioMix(new AudioSettings(), Path.Combine(Path.GetTempPath(), "nonexistent_audio_dir_" + Guid.NewGuid())))
{
    Assert(!missing.HasEngineClip && !missing.HasExplosionClip && !missing.HasWarningClip, "Missing clips should report unavailable");
    missing.PlayExplosion();
    missing.PlayBalloonPop();
    missing.StallWarning = 1f;
    missing.Read(block, 0, block.Length);
    Assert(block.All(x => float.IsFinite(x) && Math.Abs(x) <= 1f), "Missing audio fallback must remain finite");
    Assert(Energy(block) > 0, "Procedural balloon pop fallback must still work");
}

// Stress test: 34.8s with overlapping pops, throttle sweeps, and track changes.
using (var stress = new GameAudioMix(new AudioSettings()))
{
    var sw = Stopwatch.StartNew();
    for (int j = 0; j < 1500; j++)
    {
        if (j % 10 == 0) for (int n = 0; n < 8; n++) stress.PlayBalloonPop();
        if (j == 400) stress.MusicTrack = MusicTrack.Victory;
        if (j == 800) { stress.MusicTrack = MusicTrack.Death; stress.PlayExplosion(); }
        if (j == 1100) stress.MusicTrack = MusicTrack.InGame;
        stress.Throttle = (j % 100) / 100f;
        stress.Read(block, 0, block.Length);
        Assert(block.All(x => float.IsFinite(x) && Math.Abs(x) <= 1f), "Mixed samples bounded");
    }
    Console.WriteLine($"PASS WAV audio assets, effects, music crossfades, focus fade/resume and overlapping voices; rendered 34.8 seconds in {sw.Elapsed.TotalSeconds:F2}s.");
}

using (var device = new GameAudio(new AudioSettings()))
{
    device.Update(0.5f, true);
    device.PlayBalloonPop();
    Thread.Sleep(200);
}
Console.WriteLine("PASS audio device initialization/fallback and disposal.");
