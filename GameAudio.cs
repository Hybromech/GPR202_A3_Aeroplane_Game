using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace GPR202_Assessment3;

public enum MusicTrack
{
    InGame = 0,
    Victory = 1,
    Death = 2,
    Menu = 3
}

// Owns the Windows audio device. A missing device must not prevent gameplay.
public sealed class GameAudio : IDisposable
{
    private readonly GameAudioMix mix;
    private WaveOutEvent? output;

    public GameAudio(AudioSettings settings, string? audioDirectory = null)
    {
        mix = new GameAudioMix(settings, audioDirectory);
        if (!settings.Enabled || !OperatingSystem.IsWindows()) return;
        try
        {
            output = new WaveOutEvent { DesiredLatency = 80, NumberOfBuffers = 3 };
            output.Init(new SampleToWaveProvider(mix));
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

    public void SetMusicTrack(MusicTrack track) => mix.MusicTrack = track;
    public void SetVictory(bool won) => mix.Victory = won;
    public void PlayBalloonPop() => mix.PlayBalloonPop();
    public void PlayExplosion() => mix.PlayExplosion();

    public void Dispose()
    {
        try
        {
            output?.Stop();
            output?.Dispose();
        }
        catch
        {
            // Ignore audio driver shutdown errors on exit or device loss.
        }
        finally
        {
            output = null;
            mix.Dispose();
        }
    }
}

// Mixes WAV assets from Resource/Audio (streaming music tracks and cached effects),
// with procedural balloon pop fallback until an effects/pop.wav asset is added.
public sealed class GameAudioMix : ISampleProvider, IDisposable
{
    public const int OutputSampleRate = 44100;
    private const int TrackCount = 4;

    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(OutputSampleRate, 2);

    private readonly AudioSettings settings;
    private readonly CachedAudioClip? engineClip;
    private readonly CachedAudioClip? explosionClip;
    private readonly CachedAudioClip? warningClip;
    private readonly CachedAudioClip? popClip;
    private readonly StreamingWavTrack[] musicTracks;
    private readonly double[] trackGains = new double[TrackCount];
    private readonly float[][] trackBuffers = new float[TrackCount][];
    private readonly bool[] trackActive = new bool[TrackCount];

    private int activeTrack = (int)MusicTrack.InGame;
    private int previousTrack = (int)MusicTrack.InGame;

    public MusicTrack MusicTrack
    {
        get => (MusicTrack)Volatile.Read(ref activeTrack);
        set => Volatile.Write(ref activeTrack, Math.Clamp((int)value, 0, TrackCount - 1));
    }

    public bool Victory
    {
        get => MusicTrack == MusicTrack.Victory;
        set
        {
            if (value) MusicTrack = MusicTrack.Victory;
            else if (MusicTrack == MusicTrack.Victory) MusicTrack = MusicTrack.InGame;
        }
    }

    public volatile float Throttle = 0.65f;
    public volatile bool EngineRunning = true;
    public volatile float StallWarning;
    public volatile bool Focused = true;

    public bool HasEngineClip => engineClip != null;
    public bool HasExplosionClip => explosionClip != null;
    public bool HasWarningClip => warningClip != null;
    public bool HasPopClip => popClip != null;
    public bool HasMusicTrack(MusicTrack track) => musicTracks[(int)track].Available;

    private int pendingExplosion;
    private double explosionPos = -1;

    private int pendingPops;
    private readonly double[] popPos = new double[8];
    private int nextPop;

    private double enginePos;
    private double engineThrottle = 0.65;
    private double engineGain = 1.0;

    private double warningLevel;
    private double warningBeepPos;
    private double warningCycleTimer;

    private double focusGain = 1.0;
    private long frame;
    private uint noiseState = 1234567;
    private bool disposed;

    public GameAudioMix(AudioSettings settings, string? audioDirectory = null)
    {
        this.settings = settings;
        Array.Fill(popPos, -1.0);
        trackGains[(int)MusicTrack.InGame] = 1.0;
        for (int i = 0; i < TrackCount; i++) trackBuffers[i] = Array.Empty<float>();

        engineClip = CachedAudioClip.Load(ResolveAudioPath(Path.Combine("effects", "engine.wav"), audioDirectory), bakeLoop: true);
        explosionClip = CachedAudioClip.Load(ResolveAudioPath(Path.Combine("effects", "explode.wav"), audioDirectory), bakeLoop: false);
        warningClip = CachedAudioClip.Load(ResolveAudioPath(Path.Combine("effects", "warning_beep.wav"), audioDirectory), bakeLoop: false);
        popClip = CachedAudioClip.Load(ResolveAudioPath(Path.Combine("effects", "pop.wav"), audioDirectory), bakeLoop: false);

        musicTracks = new[]
        {
            new StreamingWavTrack(ResolveAudioPath(Path.Combine("music", "background_in_game.wav"), audioDirectory), OutputSampleRate),
            new StreamingWavTrack(ResolveAudioPath(Path.Combine("music", "win.wav"), audioDirectory), OutputSampleRate),
            new StreamingWavTrack(ResolveAudioPath(Path.Combine("music", "death.wav"), audioDirectory), OutputSampleRate),
            new StreamingWavTrack(ResolveAudioPath(Path.Combine("music", "menu.wav"), audioDirectory), OutputSampleRate)
        };
    }

    public void PlayBalloonPop() => Interlocked.Increment(ref pendingPops);
    public void PlayExplosion() => Interlocked.Exchange(ref pendingExplosion, 1);

    private static string? ResolveAudioPath(string relativePath, string? audioDirectory)
    {
        if (!string.IsNullOrEmpty(audioDirectory))
        {
            string explicitPath = Path.Combine(audioDirectory, relativePath);
            return File.Exists(explicitPath) ? Path.GetFullPath(explicitPath) : null;
        }

        string cwdPath = Path.Combine(Directory.GetCurrentDirectory(), "Resource", "Audio", relativePath);
        if (File.Exists(cwdPath)) return Path.GetFullPath(cwdPath);

        string basePath = Path.Combine(AppContext.BaseDirectory, "Resource", "Audio", relativePath);
        if (File.Exists(basePath)) return Path.GetFullPath(basePath);

        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int depth = 0; depth < 6 && dir != null; depth++, dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "Resource", "Audio", relativePath);
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }

        return null;
    }

    private static double Tone(double hz, double time) => Math.Sin(2 * Math.PI * hz * time);

    private double Noise()
    {
        noiseState ^= noiseState << 13;
        noiseState ^= noiseState >> 17;
        noiseState ^= noiseState << 5;
        return noiseState / (double)uint.MaxValue * 2 - 1;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        if (disposed)
        {
            Array.Clear(buffer, offset, count);
            return count;
        }

        int targetTrack = Math.Clamp(Volatile.Read(ref activeTrack), 0, TrackCount - 1);
        if (targetTrack != previousTrack)
        {
            if (targetTrack != (int)MusicTrack.InGame)
            {
                musicTracks[targetTrack].Rewind();
            }
            previousTrack = targetTrack;
        }

        if (Interlocked.Exchange(ref pendingExplosion, 0) != 0)
        {
            explosionPos = explosionClip != null ? 0.0 : -1.0;
        }

        int popEvents = Math.Min(popPos.Length, Interlocked.Exchange(ref pendingPops, 0));
        for (int j = 0; j < popEvents; j++)
        {
            popPos[nextPop] = 0.0;
            nextPop = (nextPop + 1) % popPos.Length;
        }

        double masterVol = Math.Clamp(settings.MasterVolume, 0f, 1f);
        double engineVol = Math.Clamp(settings.EngineVolume, 0f, 1f);
        double explosionVol = Math.Clamp(settings.ExplosionVolume, 0f, 1f);
        double popVol = Math.Clamp(settings.PopVolume, 0f, 1f);
        double warningVol = Math.Clamp(settings.StallWarningVolume, 0f, 1f);
        double musicVol = Math.Clamp(settings.MusicVolume, 0f, 1f);

        bool canHearAudio = (Focused && settings.Enabled) || focusGain >= 0.00001;
        Array.Clear(trackActive);
        if (canHearAudio && musicVol > 0.0)
        {
            for (int t = 0; t < TrackCount; t++)
            {
                if (!musicTracks[t].Available) continue;
                if (t == targetTrack || trackGains[t] > 0.00001)
                {
                    if (trackBuffers[t].Length < count) trackBuffers[t] = new float[count];
                    musicTracks[t].ReadStereo(trackBuffers[t], count);
                    trackActive[t] = true;
                }
            }
        }

        const double dt = 1.0 / OutputSampleRate;
        double engineBaseStep = engineClip != null ? engineClip.SampleRate / (double)OutputSampleRate : 0.0;
        double explosionStep = explosionClip != null ? explosionClip.SampleRate / (double)OutputSampleRate : 0.0;
        double warningStep = warningClip != null ? warningClip.SampleRate / (double)OutputSampleRate : 0.0;
        double warningDuration = warningClip != null ? warningClip.FrameCount / (double)warningClip.SampleRate : 0.12;
        double popStep = popClip != null ? popClip.SampleRate / (double)OutputSampleRate : 0.0;

        for (int i = 0; i < count; i += 2)
        {
            if (!Focused || !settings.Enabled)
            {
                focusGain += (0.0 - focusGain) * 0.002;
                if (focusGain < 0.00001)
                {
                    buffer[offset + i] = 0f;
                    if (i + 1 < count) buffer[offset + i + 1] = 0f;
                    continue;
                }
            }
            else
            {
                focusGain += (1.0 - focusGain) * 0.002;
            }

            double commonL = 0.0, commonR = 0.0;

            // Engine loop (pitch & level smoothly modulated by throttle).
            engineThrottle += (Throttle - engineThrottle) * 0.00005;
            engineGain += ((EngineRunning ? 1.0 : 0.0) - engineGain) * 0.002;
            if (engineClip != null && engineGain > 0.000001 && engineVol > 0.0)
            {
                engineClip.SampleLoop(enginePos, out double eL, out double eR);
                double pitch = 0.68 + 0.64 * engineThrottle;
                double level = (0.35 + 0.65 * engineThrottle) * engineGain * engineVol;
                commonL += eL * level;
                commonR += eR * level;
                enginePos += engineBaseStep * pitch;
                if (enginePos >= engineClip.FrameCount) enginePos -= engineClip.FrameCount;
            }

            // Crash explosion one-shot.
            if (explosionClip != null && explosionPos >= 0.0)
            {
                if (explosionPos < explosionClip.FrameCount)
                {
                    explosionClip.SampleClamped(explosionPos, out double bL, out double bR);
                    commonL += bL * explosionVol;
                    commonR += bR * explosionVol;
                    explosionPos += explosionStep;
                }
                else
                {
                    explosionPos = -1.0;
                }
            }

            // Stall warning beep repeating with urgency scaling from StallWarning.
            warningLevel += (StallWarning - warningLevel) * 0.001;
            if (warningClip != null && warningLevel > 0.0001)
            {
                double period = Math.Max(warningDuration + 0.06, 0.36 - 0.16 * warningLevel);
                if (warningBeepPos < warningClip.FrameCount)
                {
                    warningClip.SampleClamped(warningBeepPos, out double wL, out double wR);
                    double wGain = warningLevel * warningVol;
                    commonL += wL * wGain;
                    commonR += wR * wGain;
                    warningBeepPos += warningStep;
                }
                warningCycleTimer += dt;
                if (warningCycleTimer >= period)
                {
                    warningCycleTimer = 0.0;
                    warningBeepPos = 0.0;
                }
            }
            else if (StallWarning <= 0.0001)
            {
                warningBeepPos = 0.0;
                warningCycleTimer = 0.0;
            }

            // Balloon pop (uses effects/pop.wav if present, otherwise procedural fallback).
            if (popVol > 0.0)
            {
                if (popClip != null)
                {
                    for (int v = 0; v < popPos.Length; v++)
                    {
                        double pos = popPos[v];
                        if (pos >= 0.0)
                        {
                            if (pos < popClip.FrameCount)
                            {
                                popClip.SampleClamped(pos, out double pL, out double pR);
                                commonL += pL * popVol;
                                commonR += pR * popVol;
                                popPos[v] = pos + popStep;
                            }
                            else
                            {
                                popPos[v] = -1.0;
                            }
                        }
                    }
                }
                else
                {
                    double pop = 0.0;
                    double noise = Noise();
                    for (int v = 0; v < popPos.Length; v++)
                    {
                        double age = popPos[v];
                        if (age >= 0.0)
                        {
                            if (age < 0.3)
                            {
                                pop += (noise * 0.8 + Tone(180, age) * 0.3) * Math.Exp(-age * 32) * Math.Min(1.0, age / 0.001);
                                popPos[v] = age + dt;
                            }
                            else
                            {
                                popPos[v] = -1.0;
                            }
                        }
                    }
                    commonL += pop * popVol;
                    commonR += pop * popVol;
                }
            }

            // Crossfaded music tracks (InGame, Victory, Death, Menu).
            double musicL = 0.0, musicR = 0.0;
            double time = frame * dt;
            double musicFade = Math.Min(1.0, time / 1.0);
            double musicGain = musicVol * musicFade;
            for (int t = 0; t < TrackCount; t++)
            {
                double goal = t == targetTrack ? 1.0 : 0.0;
                trackGains[t] += (goal - trackGains[t]) * 0.00025;
                if (trackGains[t] < 0.000001 && goal == 0.0) trackGains[t] = 0.0;

                if (trackActive[t] && trackGains[t] > 0.0)
                {
                    float[] tb = trackBuffers[t];
                    musicL += tb[i] * trackGains[t];
                    if (i + 1 < count) musicR += tb[i + 1] * trackGains[t];
                }
            }

            double gain = masterVol * focusGain;
            buffer[offset + i] = (float)Math.Tanh((commonL + musicL * musicGain) * gain);
            if (i + 1 < count) buffer[offset + i + 1] = (float)Math.Tanh((commonR + musicR * musicGain) * gain);
            frame++;
        }

        return count;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var track in musicTracks)
        {
            track.Dispose();
        }
    }
}

// Decodes a WAV file once into stereo float samples and caches it by full path.
internal sealed class CachedAudioClip
{
    private static readonly ConcurrentDictionary<string, CachedAudioClip?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public int SampleRate { get; }
    public int FrameCount { get; }
    private readonly float[] samples; // interleaved stereo (L, R)

    private CachedAudioClip(int sampleRate, float[] stereoSamples)
    {
        SampleRate = sampleRate;
        samples = stereoSamples;
        FrameCount = stereoSamples.Length / 2;
    }

    public static CachedAudioClip? Load(string? fullPath, bool bakeLoop)
    {
        if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath)) return null;
        string key = (bakeLoop ? "L:" : "O:") + fullPath;
        return Cache.GetOrAdd(key, _ => Decode(fullPath, bakeLoop));
    }

    private static CachedAudioClip? Decode(string fullPath, bool bakeLoop)
    {
        try
        {
            using var reader = new WaveFileReader(fullPath);
            ISampleProvider provider = reader.ToSampleProvider();
            if (provider.WaveFormat.Channels == 1)
            {
                provider = new MonoToStereoSampleProvider(provider);
            }
            else if (provider.WaveFormat.Channels != 2)
            {
                return null;
            }

            int sampleRate = provider.WaveFormat.SampleRate;
            var allSamples = new List<float>((int)Math.Min(int.MaxValue / 4, Math.Max(4096, reader.SampleCount * 2)));
            var chunk = new float[8192];
            int read;
            while ((read = provider.Read(chunk, 0, chunk.Length)) > 0)
            {
                for (int i = 0; i < read; i++) allSamples.Add(chunk[i]);
            }

            int frames = allSamples.Count / 2;
            if (frames <= 0) return null;

            float[] stereo = allSamples.Count == frames * 2
                ? allSamples.ToArray()
                : allSamples.GetRange(0, frames * 2).ToArray();

            if (bakeLoop && frames > sampleRate * 2)
            {
                stereo = BakeSeamlessLoop(stereo, frames, sampleRate);
            }

            return new CachedAudioClip(sampleRate, stereo);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load audio clip '{fullPath}': {ex.Message}");
            return null;
        }
    }

    private static float[] BakeSeamlessLoop(float[] stereo, int frames, int sampleRate)
    {
        // Measure steady-state RMS in the middle half of the clip.
        int midStart = frames / 4;
        int midEnd = frames * 3 / 4;
        double sumSq = 0;
        for (int f = midStart; f < midEnd; f++)
        {
            float l = stereo[f * 2];
            float r = stereo[f * 2 + 1];
            sumSq += 0.5 * (l * l + r * r);
        }
        double midRms = Math.Sqrt(sumSq / Math.Max(1, midEnd - midStart));
        double threshold = midRms * 0.35;

        // Trim quiet fade-in / fade-out margins using a 25ms RMS window.
        int window = Math.Max(1, sampleRate / 40);
        int maxTrimStart = Math.Min(sampleRate / 2, frames / 8);
        int maxTrimEnd = Math.Min(sampleRate * 2, frames / 4);

        int startFrame = 0;
        while (startFrame + window < maxTrimStart && WindowRms(stereo, startFrame, window) < threshold)
        {
            startFrame += window / 2;
        }

        int endFrame = frames;
        while (endFrame - window > frames - maxTrimEnd && WindowRms(stereo, endFrame - window, window) < threshold)
        {
            endFrame -= window / 2;
        }

        int trimmedFrames = endFrame - startFrame;
        if (trimmedFrames <= sampleRate) return stereo;

        int fadeFrames = Math.Min(sampleRate / 4, trimmedFrames / 4);
        int loopFrames = trimmedFrames - fadeFrames;
        var baked = new float[loopFrames * 2];
        Array.Copy(stereo, startFrame * 2, baked, 0, loopFrames * 2);

        // Crossfade the trimmed tail into the start of the loop for a click-free wrap.
        int tailStart = startFrame + loopFrames;
        for (int f = 0; f < fadeFrames; f++)
        {
            float w = f / (float)fadeFrames;
            int headIdx = f * 2;
            int tailIdx = (tailStart + f) * 2;
            baked[headIdx] = baked[headIdx] * w + stereo[tailIdx] * (1f - w);
            baked[headIdx + 1] = baked[headIdx + 1] * w + stereo[tailIdx + 1] * (1f - w);
        }

        return baked;
    }

    private static double WindowRms(float[] stereo, int startFrame, int count)
    {
        double sumSq = 0;
        for (int f = 0; f < count; f++)
        {
            int idx = (startFrame + f) * 2;
            float l = stereo[idx];
            float r = stereo[idx + 1];
            sumSq += 0.5 * (l * l + r * r);
        }
        return Math.Sqrt(sumSq / count);
    }

    public void SampleLoop(double framePos, out double left, out double right)
    {
        int i0 = (int)framePos % FrameCount;
        if (i0 < 0) i0 += FrameCount;
        int i1 = (i0 + 1) % FrameCount;
        double frac = framePos - Math.Floor(framePos);
        int idx0 = i0 * 2;
        int idx1 = i1 * 2;
        left = samples[idx0] + (samples[idx1] - samples[idx0]) * frac;
        right = samples[idx0 + 1] + (samples[idx1 + 1] - samples[idx0 + 1]) * frac;
    }

    public void SampleClamped(double framePos, out double left, out double right)
    {
        int i0 = (int)framePos;
        if (i0 < 0 || i0 >= FrameCount)
        {
            left = 0;
            right = 0;
            return;
        }
        int i1 = Math.Min(i0 + 1, FrameCount - 1);
        double frac = framePos - i0;
        int idx0 = i0 * 2;
        int idx1 = i1 * 2;
        left = samples[idx0] + (samples[idx1] - samples[idx0]) * frac;
        right = samples[idx0 + 1] + (samples[idx1 + 1] - samples[idx0 + 1]) * frac;
    }
}

// Streams a looping stereo WAV music track lazily from disk with low memory overhead.
internal sealed class StreamingWavTrack : IDisposable
{
    private readonly string? path;
    private readonly int targetSampleRate;
    private readonly object sync = new();
    private WaveFileReader? reader;
    private ISampleProvider? provider;
    private bool failed;
    private bool disposed;

    public bool Available => !failed && !disposed && path != null;

    public StreamingWavTrack(string? fullPath, int targetSampleRate)
    {
        path = !string.IsNullOrEmpty(fullPath) && File.Exists(fullPath) ? fullPath : null;
        this.targetSampleRate = targetSampleRate;
    }

    private bool EnsureOpen()
    {
        if (provider != null) return true;
        if (failed || disposed || path == null) return false;
        try
        {
            reader = new WaveFileReader(path);
            ISampleProvider p = reader.ToSampleProvider();
            if (p.WaveFormat.Channels == 1)
            {
                p = new MonoToStereoSampleProvider(p);
            }
            else if (p.WaveFormat.Channels != 2)
            {
                throw new NotSupportedException($"Unsupported channel count: {p.WaveFormat.Channels}");
            }

            if (p.WaveFormat.SampleRate != targetSampleRate)
            {
                p = new WdlResamplingSampleProvider(p, targetSampleRate);
            }

            provider = p;
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to open music track '{path}': {ex.Message}");
            failed = true;
            reader?.Dispose();
            reader = null;
            provider = null;
            return false;
        }
    }

    public void Rewind()
    {
        lock (sync)
        {
            if (reader != null && !disposed)
            {
                reader.Position = 0;
            }
        }
    }

    public void ReadStereo(float[] dest, int count)
    {
        lock (sync)
        {
            if (!EnsureOpen() || provider == null || reader == null)
            {
                Array.Clear(dest, 0, count);
                return;
            }

            int filled = 0;
            while (filled < count)
            {
                int read = provider.Read(dest, filled, count - filled);
                if (read == 0)
                {
                    if (reader.Position == 0)
                    {
                        Array.Clear(dest, filled, count - filled);
                        break;
                    }
                    reader.Position = 0;
                    continue;
                }
                filled += read;
            }
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
            reader?.Dispose();
            reader = null;
            provider = null;
        }
    }
}
