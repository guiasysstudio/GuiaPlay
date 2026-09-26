using GuiaPlay.Core;
using LibVLCSharp.Shared;

namespace GuiaPlay.App.Playback;

internal sealed record LibVlcEqualizerCatalog(
    bool IsAvailable,
    IReadOnlyList<EqualizerPresetDefinition> Presets,
    IReadOnlyList<EqualizerBandDefinition> Bands,
    string? Error)
{
    public static LibVlcEqualizerCatalog Discover()
    {
        try
        {
            using var catalog = new Equalizer();
            var bandCount = checked((int)catalog.BandCount);
            var bands = Enumerable.Range(0, bandCount)
                .Select(index => new EqualizerBandDefinition(
                    checked((uint)index),
                    catalog.BandFrequency(checked((uint)index))))
                .ToArray();
            var presets = Enumerable.Range(0, checked((int)catalog.PresetCount))
                .Select(index => ReadPreset(catalog, checked((uint)index), bandCount))
                .Where(preset => !string.IsNullOrWhiteSpace(preset.Name))
                .ToArray();
            return new LibVlcEqualizerCatalog(true, presets, bands, null);
        }
        catch (Exception exception)
        {
            return new LibVlcEqualizerCatalog(false, [], [], exception.Message);
        }
    }

    public bool TryApply(MediaPlayer player, EqualizerConfiguration configuration, out string? error)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (!configuration.Enabled)
        {
            var disabled = player.UnsetEqualizer();
            error = disabled ? null : "O LibVLC não confirmou a desativação do equalizador.";
            return disabled;
        }

        if (!IsAvailable || Bands.Count == 0)
        {
            error = Error ?? "O equalizador não está disponível nesta instalação do LibVLC.";
            return false;
        }

        try
        {
            var normalized = EqualizerConfigurationBehavior.Normalize(configuration, Bands.Count);
            using var equalizer = new Equalizer();
            if (!equalizer.SetPreamp(normalized.Preamp))
            {
                error = "O LibVLC rejeitou a pré-amplificação do equalizador.";
                return false;
            }

            for (var index = 0; index < normalized.BandGains.Count; index++)
            {
                if (!equalizer.SetAmp(normalized.BandGains[index], checked((uint)index)))
                {
                    error = $"O LibVLC rejeitou a banda {index}.";
                    return false;
                }
            }

            var applied = player.SetEqualizer(equalizer);
            error = applied ? null : "O LibVLC não confirmou a aplicação do equalizador.";
            return applied;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }

    private static EqualizerPresetDefinition ReadPreset(Equalizer catalog, uint index, int bandCount)
    {
        using var preset = new Equalizer(index);
        return new EqualizerPresetDefinition(
            index,
            catalog.PresetName(index) ?? string.Empty,
            EqualizerConfigurationBehavior.Clamp(preset.Preamp),
            Enumerable.Range(0, bandCount)
                .Select(band => EqualizerConfigurationBehavior.Clamp(preset.Amp(checked((uint)band))))
                .ToArray());
    }
}
