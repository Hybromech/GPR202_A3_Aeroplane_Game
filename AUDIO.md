# Game audio

GameAudio uses NAudio 2.2.1 with the Windows default output device. The game continues silently if audio cannot initialize or if any audio file is missing. Audio fades out when the window loses focus, resumes on focus, and is disposed on exit.

AudioSettings.cs controls Enabled, MasterVolume, EngineVolume, ExplosionVolume, PopVolume, StallWarningVolume, and MusicVolume (volumes 0–1). Game.cs owns the settings instance.

## Audio assets (`Resource/Audio`)

- **Engine (`effects/engine.wav`)**: Cached into memory with a seamless loop crossfade. Pitch and level change smoothly with throttle, including idle at zero throttle, and fade out on crash.
- **Explosion (`effects/explode.wav`)**: One-shot blast played on terrain impact.
- **Stall warning (`effects/warning_beep.wav`)**: Repeating warning beep whose rate and level increase as angle of attack approaches stall, fading out on recovery.
- **Balloon pop (`effects/pop.wav` optional)**: Uses `effects/pop.wav` if present, otherwise falls back to the procedural pop effect. Up to eight overlapping pops are mixed.
- **Music (`music/background_in_game.wav`, `music/win.wav`, `music/death.wav`, `music/menu.wav`)**: Streamed lazily from disk and smoothly crossfaded via `MusicTrack`:
  - `InGame` (`background_in_game.wav`) loops during normal flight.
  - `Victory` (`win.wav`) crossfades in when all balloons are collected.
  - `Death` (`death.wav`) crossfades in on crash during the three-second restart sequence; restarting restores `InGame`.
  - `Menu` (`menu.wav`) is wired into `MusicTrack` and ready for future menu integration.

Run automated checks: `dotnet run --project tests/AudioChecks/AudioChecks.csproj -c Release`
