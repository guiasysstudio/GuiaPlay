namespace GuiaPlay.Core;

/// <summary>
/// Conservative requirements for the official YouTube embedded player, as
/// published in its Required Minimum Functionality on 2026-10-15.
/// UI dimensions here are actual physical pixels, not WPF device-independent units.
/// </summary>
public static class YouTubeEmbedGate
{
    public const int MinimumViewportWidthPixels = 200;
    public const int MinimumViewportHeightPixels = 200;
    public const int RecommendedViewportWidthPixels = 480;
    public const int RecommendedViewportHeightPixels = 270;

    public static YouTubeEmbedAssessment Assess(
        int widthPixels,
        int heightPixels,
        bool hasAppIdentification,
        bool isActuallyVisible,
        bool automaticPlayback,
        double visibleViewportFraction,
        int otherAutoPlayingYouTubePlayersOnSameScreen)
    {
        if (widthPixels < MinimumViewportWidthPixels ||
            heightPixels < MinimumViewportHeightPixels)
        {
            return new(false, "A janela do player oficial exige pelo menos 200 × 200 pixels.");
        }

        if (!hasAppIdentification)
        {
            return new(false, "Origem/identificador do aplicativo (HTTP Referer) não configurado.");
        }

        if (!isActuallyVisible)
        {
            return new(false, "O player oficial não pode reproduzir oculto.");
        }

        if (automaticPlayback && (double.IsNaN(visibleViewportFraction) ||
            visibleViewportFraction <= 0.5))
        {
            return new(false, "Reprodução automática requer mais da metade do player visível.");
        }

        if (automaticPlayback && otherAutoPlayingYouTubePlayersOnSameScreen > 0)
        {
            return new(false, "Só um player do YouTube pode iniciar automaticamente na mesma tela.");
        }

        return new(true, null);
    }

    public static bool CanMirrorAsWpfFrames => false;

    public static bool GuaranteesMultipleOutputSynchronization => false;
}

public sealed record YouTubeEmbedAssessment(bool Allowed, string? Reason);
