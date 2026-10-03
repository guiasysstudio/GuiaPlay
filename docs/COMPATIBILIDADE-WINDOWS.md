# Compatibilidade Windows do GuiaPlay

Estado desta matriz: **2026-10-02**. Ela separa suporte documental da stack, validação automatizada neste computador e validação física ainda necessária. Um build que compila não é registrado como teste físico.

## Declaração de suporte

Para a linha 0.10.3/RC1, a base mínima planejada do GuiaPlay é:

> Suportado oficialmente a partir do Windows 10 Enterprise LTSC 2019, versão 1809, build 17763, em uma arquitetura publicada e enquanto o sistema operacional estiver no ciclo de suporte da Microsoft. Windows 11 é suportado nas versões e edições ainda atendidas pela Microsoft e pelo .NET 10.

Essa declaração somente deve sair de “planejada” para “validada” depois do release gate físico nas máquinas alvo. Windows 10 Home/Pro 22H2 não integra a matriz oficial: o sistema encerrou o suporte em 2025-10-14 e não aparece na matriz atual do .NET 10. Windows 7, 8 e 8.1 não são suportados.

Fontes primárias:

- [.NET 10 — sistemas operacionais suportados](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)
- [Instalação do .NET no Windows — tabela de versões suportadas](https://learn.microsoft.com/dotnet/core/install/windows)
- [Versões de cliente Windows ainda suportadas](https://learn.microsoft.com/windows/release-health/supported-versions-windows-client)
- [Informações de versão do Windows 11](https://learn.microsoft.com/windows/release-health/windows11-release-information)
- [Visão geral do Windows Enterprise LTSC](https://learn.microsoft.com/windows/whats-new/ltsc/overview)

## Matriz real da stack

| Windows / build base | Edição | Arquitetura | .NET 10 em 2026-10-02 | Status GuiaPlay 0.10.3 | Teste realizado | Observações |
|---|---|---:|---|---|---|---|
| Windows 10 1607 / 14393 | Enterprise LTSB/LTSC 2016 | x64, x86 | Sim, até o fim do ciclo do SO | Não é base oficial | Nenhum | O suporte do Windows termina em 2026-10-13; não é responsável lançar uma nova linha com apenas dias de vida útil. |
| Windows 10 1809 / 17763 | Enterprise LTSC 2019 | x64, x86 | Sim | Base mínima planejada | Pendente em máquina real | Ciclo estendido até 2029. O instalador deve bloquear builds anteriores. |
| Windows 10 21H2 / 19044 | Enterprise LTSC 2021 e IoT LTSC | x64, x86 | Sim | Planejado | Pendente em máquina real | Enterprise LTSC 2021 termina em 2027; IoT possui ciclo diferente. |
| Windows 10 22H2 / 19045 | Home, Pro, Enterprise GA | x64, x86 | Não na matriz atual | Não suportado oficialmente | Nenhum | Fim do suporte geral em 2025-10-14. Execução incidental não equivale a suporte. |
| Windows 11 23H2 / 22631 | Enterprise/Education | x64; x86 via WOW64 | Sim | Planejado | Pendente em máquina real | Home/Pro 23H2 já encerrou suporte; somente Ent/Edu permanece elegível. |
| Windows 11 24H2 / 26100 | Home/Pro e Enterprise/Education enquanto atendidos | x64; x86 via WOW64 | Sim | Planejado | Pendente em máquina real | Home/Pro encerra suporte em 2026-10-13; Enterprise/Education continua depois dessa data. |
| Windows 11 25H2 / 26200 | Home/Pro/Enterprise/Education | x64; x86 via WOW64 | Sim | Validado apenas no ambiente local | Build/testes e bootstrap x64/x86 registrados no relatório M10.3; UI/playback físico pendente | Ambiente desta execução: Windows 11 Pro 25H2, build 26200.9457, SO x64. Uma instância 0.10.2 já aberta impediu isolar a janela da candidata. |
| Windows 11 26H1 / 28000 | Edições suportadas | x64; x86 via emulação | Sim | Compatível documentalmente, não validado | Nenhum | Versão destinada principalmente a novos dispositivos; não é requisito da RC1. |
| Windows 11 26H2 / 26300 | Edições suportadas | x64 | A lista do .NET 10, atualizada em 2026-09-28, ainda não a relaciona | Pendente | Nenhum | O Windows foi disponibilizado em 2026-09-29. Não declarar suporte até a Microsoft atualizar a matriz do runtime e existir teste. |

## Arquiteturas

- **win-x64** é a distribuição principal, self-contained, sem exigir instalação separada do .NET.
- **win-x86** é tecnicamente possível: o pacote restaurado `VideoLAN.LibVLC.Windows 3.0.24` contém `build/x86/libvlc.dll`, `libvlccore.dll` e plugins x86 separados dos equivalentes x64. A publicação e o instalador devem manter RIDs e marcadores distintos.
- O .NET 10 documenta x86 em Windows e emulação x86 sobre x64. Windows 11 não possui uma edição cliente x86 comum; o pacote x86 nesse sistema roda por WOW64/emulação e continua sujeito ao gate físico.
- **win-arm64** não faz parte deste marco, embora o pacote LibVLC contenha arquivos Arm64. Não há artefato, instalador nem validação GuiaPlay Arm64 nesta entrega.

Nunca se instala automaticamente um pacote de RID diferente do marcador da instalação atual.

## Dependências e hardware

O GuiaPlay depende de:

- WPF e Windows Desktop Runtime incluídos no publish self-contained;
- LibVLC 3.0.24 e seus plugins nativos da mesma arquitetura do processo;
- drivers funcionais de vídeo, áudio e monitor fornecidos ao Windows;
- memória suficiente para Windows, buffers de vídeo e mídia reproduzida.

O código do GuiaPlay não seleciona fabricante de CPU, GPU, placa-mãe ou monitor. Não existe requisito de GPU dedicada nem contagem fixa de núcleos. WPF pode recorrer à renderização de software quando a aceleração disponível é insuficiente, com possível perda de desempenho. Decodificação, 4K, Bluetooth, HDMI, USB, múltiplos DPIs e limite de endereço do processo x86 exigem teste físico; não são inferidos a partir do build.

## Gate físico antes da RC1

Permanecem obrigatórios, sem marcação automática:

- Windows 10 LTSC 2019/2021 reais;
- Windows 11 23H2 Enterprise/Education e 24H2, quando disponíveis, além do 25H2 local;
- x64 e x86/WOW64;
- Intel e AMD; vídeo integrado e GPU dedicada quando disponíveis;
- 1, 2 e 3 monitores, coordenadas negativas, orientação e DPI 100/125/150%;
- áudio onboard, HDMI, USB e Bluetooth;
- reprodução 720p, 1080p e 4K, medindo RAM e resposta a falta de memória;
- instalação, desinstalação, Explorer e atualização real 0.10.2 → 0.10.3.
