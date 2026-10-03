# Branding oficial do GuiaPlay

Os únicos arquivos-fonte da identidade são:

- `src/GuiaPlay.App/Assets/Branding/Source/GuiaPlay-Logo.svg` — símbolo oficial;
- `src/GuiaPlay.App/Assets/Branding/Source/GuiaPlay-Wordmark.svg` — escrita oficial.

Não redesenhe, recolora, estique nem substitua esses SVGs por uma versão raster. PNG, ICO e BMP são derivados descartáveis e reproduzíveis.

## Regenerar os derivados

Na raiz do repositório, execute:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\generate-branding-assets.ps1
```

O script usa o Microsoft Edge instalado no Windows somente como renderizador de SVG. Redimensionamento, composição, ICO multirresolução e BMP 24 bits são produzidos com APIs do próprio Windows/.NET; nada é adicionado ao runtime do GuiaPlay. É possível informar outro executável compatível por `-EdgePath`.

O processo trabalha em uma pasta temporária, valida dimensão e conteúdo antes de substituir os arquivos versionados e atualiza `docs/branding/brand-manifest.json` com dimensões e SHA-256. Uma falha de renderização não deve sobrescrever parcialmente o pacote oficial.

## Uso no aplicativo

- `Assets/Branding/Icons/GuiaPlay.ico`: executável, janela, taskbar e atalhos;
- `Assets/Branding/GuiaPlay-Wordmark-UI.png`: cabeçalho da interface;
- `Assets/Branding/GuiaPlay-Icon-UI.png`: símbolo raster para superfícies que não consomem SVG;
- `Assets/Branding/Icons/GuiaPlay-Icon-{16,24,32,48,64,128,256,512}.png`: tamanhos auxiliares;
- `Assets/Branding/Wordmarks/`: wordmarks raster de documentação/UI.

## Uso no Inno Setup

- `installer/Assets/GuiaPlay-Setup.ico`: ícone do Setup;
- `installer/Assets/WizardImageFile.bmp`: imagem lateral 164 × 314 em BMP 24 bits;
- `installer/Assets/WizardSmallImageFile.bmp`: imagem de cabeçalho 55 × 58 em BMP 24 bits;
- `installer/Assets/GuiaPlay-Banner-700x200.png`: composição auxiliar fiel aos dois SVGs.

Os fundos claros das composições do instalador são apenas superfície; símbolo e wordmark mantêm as cores e proporções definidas nos SVGs.

## Gate de release

O build de release deve falhar se qualquer SVG fonte ou derivado obrigatório estiver ausente/vazio. Antes de publicar:

1. execute o gerador;
2. confira que `git diff` não revela deriva inesperada;
3. abra visualmente wordmark, ícone e imagens do assistente;
4. compile o aplicativo e o instalador;
5. valide ícone do EXE, atalhos, taskbar, Adicionar/Remover Programas e Setup em DPI 100/125/150%.
