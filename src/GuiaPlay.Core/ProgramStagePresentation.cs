namespace GuiaPlay.Core;

/// <summary>
/// Text and visibility for the public-program transmission mirror.
/// The mirror is fed from the same frame as the actual output windows and is
/// never presented as a physical screen capture.
/// </summary>
public sealed record ProgramStagePresentation(
    string Badge,
    string Message,
    string Hint,
    bool ShowFrame,
    string OperatorBadge);

public static class ProgramStagePresentationPolicy
{
    public static ProgramStagePresentation Resolve(
        PlaybackStatus playbackStatus,
        MediaKind mediaKind,
        int selectedPublicOutputs,
        int activePublicOutputs,
        bool frameReady)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(selectedPublicOutputs);
        ArgumentOutOfRangeException.ThrowIfNegative(activePublicOutputs);

        var operatorBadge = playbackStatus switch
        {
            PlaybackStatus.Preparing => "CARREGANDO",
            PlaybackStatus.Playing => "REPRODUZINDO",
            PlaybackStatus.Paused => "PAUSADO",
            PlaybackStatus.Ready => "PRONTO",
            PlaybackStatus.Stopped => "PARADO",
            PlaybackStatus.Ended => "CONCLUÍDO",
            PlaybackStatus.Error => "ERRO",
            _ => "AGUARDANDO"
        };

        if (mediaKind == MediaKind.Audio && playbackStatus != PlaybackStatus.Empty)
        {
            return new ProgramStagePresentation(
                "SOMENTE ÁUDIO",
                "Reprodução de áudio",
                "Áudio não gera imagem nas telas públicas.",
                false, operatorBadge);
        }

        if (activePublicOutputs == 0)
        {
            return new ProgramStagePresentation(
                "SEM TRANSMISSÃO",
                "Nenhuma tela pública em exibição",
                selectedPublicOutputs == 0
                    ? "Selecione uma tela pública e inicie o vídeo."
                    : "Tela selecionada para a próxima reprodução.",
                false, operatorBadge);
        }

        if (!frameReady)
        {
            return new ProgramStagePresentation(
                "PREPARANDO",
                "Aguardando quadro do vídeo",
                $"{activePublicOutputs} saída(s) aberta(s); aguardando o primeiro quadro.",
                false, operatorBadge);
        }

        return new ProgramStagePresentation(
            playbackStatus == PlaybackStatus.Paused ? "PAUSADO" : "AO VIVO",
            string.Empty,
            $"{activePublicOutputs} saída(s) pública(s) com imagem.",
            true,
            operatorBadge);
    }
}
