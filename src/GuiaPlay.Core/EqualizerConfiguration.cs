namespace GuiaPlay.Core;

public sealed record EqualizerConfiguration(
    bool Enabled,
    string PresetName,
    float Preamp,
    IReadOnlyList<float> BandGains)
{
    public const string CustomPresetName = "Personalizado";
    public const float MinimumGain = -20f;
    public const float MaximumGain = 20f;

    public static EqualizerConfiguration Default { get; } = new(false, "Flat", 0f, []);
}

public sealed record EqualizerPresetDefinition(
    uint Index,
    string Name,
    float Preamp,
    IReadOnlyList<float> BandGains);

public sealed record EqualizerBandDefinition(uint Index, float FrequencyHz);

public static class EqualizerConfigurationBehavior
{
    public static EqualizerConfiguration Normalize(EqualizerConfiguration? configuration, int bandCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bandCount);
        var source = configuration ?? EqualizerConfiguration.Default;
        var preset = string.IsNullOrWhiteSpace(source.PresetName) ? "Flat" : source.PresetName.Trim();
        var gains = new float[bandCount];
        for (var index = 0; index < gains.Length; index++)
        {
            gains[index] = Clamp(index < source.BandGains.Count ? source.BandGains[index] : 0f);
        }

        return new EqualizerConfiguration(source.Enabled, preset, Clamp(source.Preamp), gains);
    }

    public static EqualizerConfiguration FromPreset(EqualizerPresetDefinition preset, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(preset);
        return new EqualizerConfiguration(
            enabled,
            preset.Name,
            Clamp(preset.Preamp),
            preset.BandGains.Select(Clamp).ToArray());
    }

    public static EqualizerConfiguration WithBandGain(
        EqualizerConfiguration configuration,
        int bandIndex,
        float gain,
        int bandCount)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentOutOfRangeException.ThrowIfNegative(bandIndex);
        if (bandIndex >= bandCount)
        {
            throw new ArgumentOutOfRangeException(nameof(bandIndex));
        }

        var normalized = Normalize(configuration, bandCount);
        var gains = normalized.BandGains.ToArray();
        gains[bandIndex] = Clamp(gain);
        return normalized with
        {
            PresetName = EqualizerConfiguration.CustomPresetName,
            BandGains = gains
        };
    }

    public static EqualizerConfiguration WithPreamp(
        EqualizerConfiguration configuration,
        float preamp,
        int bandCount) =>
        Normalize(configuration, bandCount) with
        {
            PresetName = EqualizerConfiguration.CustomPresetName,
            Preamp = Clamp(preamp)
        };

    public static float Clamp(float value) =>
        float.IsFinite(value)
            ? Math.Clamp(value, EqualizerConfiguration.MinimumGain, EqualizerConfiguration.MaximumGain)
            : 0f;
}
