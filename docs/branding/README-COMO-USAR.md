# GuiaPlay — Pacote de Branding

Este pacote já está organizado para você extrair **na raiz do projeto**:

`E:\Projetos\GuiaSys\GuiaPlay\GuiaPlay`

## Pastas criadas

- `src/GuiaPlay.App/Assets/Branding/Icons/`
- `src/GuiaPlay.App/Assets/Branding/Wordmarks/`
- `installer/Assets/`

## Arquivos principais

### Aplicativo

- `src/GuiaPlay.App/Assets/Branding/Icons/GuiaPlay.ico`
  - Ícone principal do programa (janela, taskbar, atalhos e executável quando o projeto apontar para esse .ico).
- `src/GuiaPlay.App/Assets/Branding/Icons/GuiaPlay-Icon-16.png` até `GuiaPlay-Icon-512.png`
  - PNGs auxiliares em vários tamanhos.
- `src/GuiaPlay.App/Assets/Branding/GuiaPlay-Wordmark-UI.png`
  - Versão pronta da escrita para uso na interface.
- `src/GuiaPlay.App/Assets/Branding/Wordmarks/GuiaPlay-Wordmark-1024.png`
  - Melhor arquivo base para a escrita na UI.

### Instalador (Inno Setup)

- `installer/Assets/GuiaPlay-Setup.ico`
  - Ícone do instalador.
- `installer/Assets/WizardImageFile.bmp`
  - Imagem lateral do assistente.
- `installer/Assets/WizardSmallImageFile.bmp`
  - Imagem pequena do cabeçalho do assistente.
- `installer/Assets/GuiaPlay-Banner-700x200.png`
  - Banner extra opcional para telas customizadas ou documentação.

## Sugestão de uso pelo Codex

### No app WPF
Usar principalmente:
- `src/GuiaPlay.App/Assets/Branding/Icons/GuiaPlay.ico`
- `src/GuiaPlay.App/Assets/Branding/GuiaPlay-Wordmark-UI.png`

### No instalador Inno Setup
Usar principalmente:
- `installer/Assets/GuiaPlay-Setup.ico`
- `installer/Assets/WizardImageFile.bmp`
- `installer/Assets/WizardSmallImageFile.bmp`

## Observações

- Os PNGs têm fundo transparente.
- O arquivo `.ico` já foi gerado em múltiplas resoluções.
- Os BMPs já estão prontos para o Inno Setup.
- Mantive também os originais em PNG dentro das pastas de branding.
