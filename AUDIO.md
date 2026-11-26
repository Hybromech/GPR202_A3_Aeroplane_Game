# Game audio

GameAudio uses NAudio 2.2.1 with the Windows default output device. The game continues silently if audio cannot initialize. Audio fades out when the window loses focus, resumes on focus, and is disposed on exit.

AudioSettings.cs controls Enabled, MasterVolume, EngineVolume, PopVolume and MusicVolume (volumes 0–1). Game.cs owns the settings instance.

The engine loops continuously and smoothly changes pitch and level with throttle, including an idle sound at zero throttle. One pop is triggered when a balloon first becomes collected; up to eight overlapping effects are mixed. Background music begins when loading finishes and repeats a four-chord cinematic-style synth arrangement with pad, bass, arpeggio and percussion. All sounds are original procedural synthesis, not sampled aircraft recordings or orchestral recordings; no audio assets need downloading.

Run automated checks: dotnet run --project tests/AudioChecks/AudioChecks.csproj -c Release
