using System;
using System.Threading;
using NAudio.Wave;

namespace GPR202_Assessment3;

// Owns the Windows audio device. A missing device must not prevent gameplay.
public sealed class GameAudio : IDisposable
{
    private readonly GameAudioMix mix;
    private WaveOutEvent? output;
    public GameAudio(AudioSettings settings)
    {
        mix = new GameAudioMix(settings);
        if (!settings.Enabled || !OperatingSystem.IsWindows()) return;
        try
        {
            output = new WaveOutEvent { DesiredLatency = 80, NumberOfBuffers = 3 };
            output.Init(new NAudio.Wave.SampleProviders.SampleToWaveProvider(mix));
            output.Play();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Audio unavailable ({ex.GetType().Name}); continuing silently.");
            Dispose();
        }
    }
    public void Update(float throttle, bool focused, float stallWarning = 0f, bool engineRunning = true)
    {
        mix.Throttle = Math.Clamp(throttle, 0f, 1f);
        mix.Focused = focused;
        mix.EngineRunning = engineRunning;
        mix.StallWarning = Math.Clamp(stallWarning, 0f, 1f);
    }
    public void SetVictory(bool won) => mix.Victory = won;
    public void PlayBalloonPop() => mix.PlayBalloonPop();
    public void PlayExplosion() => mix.PlayExplosion();
    public void Dispose()
    {
        output?.Stop();
        output?.Dispose();
        output = null;
    }
}

// Original procedural soundtrack and effects; no external recordings required.
// All oscillator/voice state belongs to the audio thread. Game events use atomics.
public sealed class GameAudioMix : ISampleProvider
{
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
    private readonly AudioSettings settings;
    public volatile bool Victory;
    private bool previousVictory;
    private double victoryTime, victoryBlend;
    private static readonly int[] VictoryMelody = {72,76,79,84,83,79,76,79,77,81,84,81,79,76,74,72};
    public volatile float Throttle = 0.65f;
    public volatile bool EngineRunning = true;
    public volatile float StallWarning;
    private int pendingExplosion;
    private double explosionAge = 10, rumble, engineGain = 1;
    public void PlayExplosion() => Interlocked.Exchange(ref pendingExplosion, 1);
    private double warningLevel;
    public volatile bool Focused = true;
    private int pendingPops;
    private readonly double[] popAge = new double[8];
    private int nextPop;
    private long frame;
    private double enginePhase, engineThrottle = 0.65, focusGain = 1;
    private uint noiseState = 1234567;
    private static readonly int[][] Chords = { new[]{45,48,52}, new[]{41,45,48}, new[]{48,52,55}, new[]{43,47,50} };
    public GameAudioMix(AudioSettings settings)
    {
        this.settings = settings;
        Array.Fill(popAge, 1.0);
    }
    public void PlayBalloonPop() => Interlocked.Increment(ref pendingPops);
    private static double Tone(double hz, double time) => Math.Sin(2 * Math.PI * hz * time);
    private static double Hz(int note) => 440 * Math.Pow(2, (note - 69) / 12.0);
    private double Noise()
    {
        noiseState ^= noiseState << 13; noiseState ^= noiseState >> 17; noiseState ^= noiseState << 5;
        return noiseState / (double)uint.MaxValue * 2 - 1;
    }
    public int Read(float[] buffer, int offset, int count)
    {
        if (Victory && !previousVictory) victoryTime = 0;
        previousVictory = Victory;
        if (Interlocked.Exchange(ref pendingExplosion, 0) != 0) explosionAge = 0;
        int events = Math.Min(8, Interlocked.Exchange(ref pendingPops, 0));
        for (int j = 0; j < events; j++) { popAge[nextPop] = 0; nextPop = (nextPop + 1) % popAge.Length; }
        const double dt = 1.0 / 44100;
        for (int i = 0; i < count; i += 2)
        {
            if (!Focused || !settings.Enabled)
            {
                focusGain += (0 - focusGain) * 0.002;
                if (focusGain < 0.00001) { buffer[offset+i] = 0; if(i+1<count) buffer[offset+i+1]=0; continue; }
            }
            else focusGain += (1 - focusGain) * 0.002;
            engineThrottle += (Throttle-engineThrottle) * 0.00005;
            enginePhase = (enginePhase + (35 + 50*engineThrottle)*dt) % 1;
            double noise = Noise();
            double engine = (Math.Sin(2*Math.PI*enginePhase) + 0.4*Math.Sin(4*Math.PI*enginePhase)
                + 0.2*Math.Sin(10*Math.PI*enginePhase) + noise*0.12) * (0.3+0.35*engineThrottle);
            double time = frame*dt;
            // Four 4-second chords, layered strings/pad, bass, arpeggio and soft timpani pulse.
            double section = time % 16, local = section % 4;
            int chord = (int)(section/4), previous = (chord+3)%4;
            double crossfade = Math.Min(1, local/0.35);
            double left = 0, right = 0;
            for (int n = 0; n < 3; n++)
            {
                double hz = Hz(Chords[chord][n]+12), oldHz = Hz(Chords[previous][n]+12);
                left += (Tone(hz,time)*crossfade+Tone(oldHz,time)*(1-crossfade))*0.16;
                right += (Tone(hz*1.002,time)*crossfade+Tone(oldHz*1.002,time)*(1-crossfade))*0.16;
            }
            double beat = time % 0.5;
            double arp = Tone(Hz(Chords[chord][(int)(local/0.5)%3]+24),beat)*Math.Exp(-beat*8)*Math.Min(1,beat/0.015)*0.15;
            double bass = (Tone(Hz(Chords[chord][0]-12),time)*crossfade
                + Tone(Hz(Chords[previous][0]-12),time)*(1-crossfade))*0.17;
            double drumAge = time%2;
            double drum = Math.Sin(2*Math.PI*(48*drumAge+3*(1-Math.Exp(-drumAge*15))))*Math.Exp(-drumAge*7)*0.15;
            double musicFade = Math.Min(1,time/2);
            double pop = 0;
            for (int v = 0; v < popAge.Length; v++)
            {
                double age = popAge[v];
                if (age < 0.3) { pop += (noise*0.8+Tone(180,age)*0.3)*Math.Exp(-age*32)*Math.Min(1,age/0.001); popAge[v]+=dt; }
            }
            warningLevel += (StallWarning-warningLevel)*0.001;
            double warning = Tone(850,time) * (0.5+0.5*Tone(4,time)) * warningLevel * Math.Clamp(settings.StallWarningVolume,0,1);
            engineGain += ((EngineRunning ? 1 : 0)-engineGain)*0.002;
            rumble += (noise-rumble)*0.035;
            double blast = 0;
            if (explosionAge < 2.5) {
                blast = (noise*Math.Exp(-explosionAge*14)*0.6 + rumble*Math.Exp(-explosionAge*2)*2
                    + Tone(45,explosionAge)*Math.Exp(-explosionAge*3)*0.35)*Math.Min(1,explosionAge/0.002);
                explosionAge += dt;
            }
            double common = blast*Math.Clamp(settings.ExplosionVolume,0,1) + warning + engine*engineGain*Math.Clamp(settings.EngineVolume,0,1) + pop*Math.Clamp(settings.PopVolume,0,1);
            double gain = Math.Clamp(settings.MasterVolume,0,1)*focusGain;
            double musicGain = Math.Clamp(settings.MusicVolume,0,1)*musicFade;
            // Crossfade to an original major-key brass-like victory fanfare.
            victoryBlend += ((Victory ? 1 : 0)-victoryBlend)*0.0001;
            double noteTime = victoryTime % 0.3;
            int note = VictoryMelody[(int)(victoryTime/0.3)%VictoryMelody.Length];
            double envelope = Math.Min(1,noteTime/0.025)*Math.Min(1,(0.3-noteTime)/0.06);
            double fanfare = (Tone(Hz(note),victoryTime)+0.3*Tone(Hz(note)*2,victoryTime))*envelope*0.28;
            fanfare += (Tone(Hz(48),victoryTime)+Tone(Hz(52),victoryTime)+Tone(Hz(55),victoryTime))*0.1;
            if (Victory || victoryBlend>0.0001) victoryTime += dt;
            left = (left+arp+bass+drum)*(1-victoryBlend)+fanfare*victoryBlend;
            right = (right+arp+bass+drum)*(1-victoryBlend)+fanfare*victoryBlend;
            buffer[offset+i] = (float)Math.Tanh((common+left*musicGain)*gain);
            if(i+1<count) buffer[offset+i+1] = (float)Math.Tanh((common+right*musicGain)*gain);
            frame++;
        }
        return count;
    }
}
