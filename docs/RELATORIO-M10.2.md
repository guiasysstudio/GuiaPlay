# Relatório M10.2 — Polimento profundo da interface

Versão: **0.10.2-prototipo**

Data da versão: **26/09/2026**

Canal: **Prototype**

## Escopo

Esta entrega corrige a navegação lateral de Configurações e refina a densidade/hierarquia visual da Interface 2.0. Não adiciona recurso operacional, não altera o schema 6, playback, equalizador, integração Windows, instância única ou updater, e não inicia `1.0.0-rc1`.

## Sidebar estrutural

A antiga composição colocava um `TabControl` de headers à esquerda sobre dois fundos visuais, permitindo que o chrome padrão de `TabItem` atravessasse o limite da sidebar. A nova estrutura possui três colunas reais:

- sidebar fixa de 192 DIPs;
- gap de 12 DIPs;
- superfície de conteúdo independente.

Sete `RadioButton` acessíveis controlam um `TabControl` interno cujo template apresenta apenas `SelectedContent`; headers nativos não são renderizados. Cada item tem ícone Fluent, padding interno, hover sutil, fundo selecionado, indicador vertical de accent e borda de foco dentro do próprio template. A região da sidebar ainda usa `ClipToBounds=True` como proteção adicional.

## Janela principal

O painel operacional foi compactado de uma estimativa estrutural de aproximadamente 138 DIPs para 108–112 DIPs, redução próxima de 20–22%. Padding passou de `14,12` para `10,8`, gaps verticais de 9 para 6 DIPs, botões operacionais de 38 para 34 DIPs e o indicador do operador de `10,6` para `8,4`. A janela não cresceu; aproximadamente 26–30 DIPs foram devolvidos à linha flexível que contém Prévia e Playlist.

A Playlist passou a ter ações de grupo na mesma command bar do cabeçalho, contador derivado por grupo, ícones de áudio/vídeo, seleção tematizada e estado vazio orientativo. Operador e saídas continuam semanticamente separados; saídas usam chips selecionáveis com foco visível. A Prévia ganhou estado vazio com orientação e mantém `Background="Black"` na área de vídeo.

Cabeçalho, cards, status, controles e seleção continuam consumindo os `DynamicResource` semânticos do M10.1. Alto contraste e troca de tema do modo Sistema permanecem governados pela infraestrutura existente.

## Acessibilidade e layout

- navegação lateral usa controles focáveis com nome de automação;
- Tab/Shift+Tab e ativação por teclado permanecem nativos;
- foco, hover e seleção ficam contidos no template e no clipping da sidebar;
- botões compactos mantêm 34 DIPs e sliders não foram afinados;
- colunas reais e scroll horizontal das saídas protegem os layouts mínimos e DPI alto;
- roteiro físico cobre 1366 × 768, 1920 × 1080 e DPI 100%, 125% e 150%.

## Validação automatizada

O pipeline oficial `scripts/build-release.ps1` concluiu com sucesso:

- `dotnet restore`: aprovado;
- `dotnet format --verify-no-changes`: aprovado;
- build Debug e Release: 0 erros e 0 warnings;
- testes Debug: 211/211 aprovados;
- testes Release: 211/211 aprovados;
- stress em dez repetições: 40/40 cenários críticos e 20/20 contratos estruturais M10.2, total 60/60;
- smoke test do executável Debug: processo inicializado e permaneceu estável por quatro segundos.

Os testes estruturais comprovam a separação sidebar/conteúdo, largura/gap, clipping, ausência visual dos headers nativos, foco contido, recursos dinâmicos, métricas compactas, estados vazios, operações principais e preservação do fundo preto da Prévia.

## Artefatos

- ZIP: `GuiaPlay-0.10.2-prototipo-win-x64.zip` — 240.103.801 bytes;
- Setup: `GuiaPlay-Setup-0.10.2-prototipo.exe` — 151.139.735 bytes;
- ZIP SHA-256: `4d496d4c840da76ebf1367fd42a3427ed4bb85b4ad14fcf49408420328691d37`;
- Setup SHA-256: `0e06d51c9602976281b265e310f482260c92fb1f5c2c3679d54904965482e436`.

O ZIP contém 1.667 entradas e `GuiaPlay.exe`; o executável informa `ProductVersion` **0.10.2-prototipo** e `FileVersion` **0.10.2.0**.

## Validação física pendente

O controlador disponível nesta sessão expôs apenas navegador e não forneceu uma superfície de aplicativos nativos. Permanecem para Andrew a inspeção visual de hover/seleção/foco, DPI, temas/alto contraste, proporção final do painel e playback em telas reais, conforme `docs/ROTEIRO-VALIDACAO-MANUAL.md`.
