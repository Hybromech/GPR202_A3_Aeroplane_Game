namespace GPR202_Assessment3;

public sealed class AudioSettings
{
    public bool Enabled { get; set; } = true;
    public float MasterVolume { get; set; } = 0.65f;
    public float EngineVolume { get; set; } = 0.35f;
    public float ExplosionVolume { get; set; } = 0.9f;
    public float PopVolume { get; set; } = 0.8f;
    public float StallWarningVolume { get; set; } = 0.25f;
    public float MusicVolume { get; set; } = 0.3f;
}
