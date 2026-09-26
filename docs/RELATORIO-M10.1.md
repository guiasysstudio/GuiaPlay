# Relatório M10.1 — Paletas completas da interface

Versão: **0.10.1-prototipo**

Data da versão: **26/09/2026**

Canal: **Prototype**

## Escopo

Esta entrega é exclusivamente uma correção visual do M10 e uma versão intermediária para validar fisicamente o update automático `0.10.0-prototipo → 0.10.1-prototipo`. Não adiciona recursos, não altera o schema 6, o equalizador ou a integração Windows e não inicia `1.0.0-rc1`.

## Causa e correção

No M10, somente accent, foreground, superfície sutil e borda eram resolvidos por cor. Cards e fundos principais ainda consumiam diretamente recursos genéricos do Fluent, fazendo Azul, Ciano, Roxo, Verde, Laranja e Rosa parecerem quase iguais fora dos botões.

`AccentPalette` agora representa uma paleta semântica completa:

- accent, foreground, subtle e border;
- fundo da janela;
- superfícies primária, secundária e elevada;
- sidebar;
- superfície e hover de controles;
- seleção e seu foreground;
- divisor/borda;
- texto primário e secundário.

Há valores explícitos e previsíveis para cada uma das seis cores nos modos Claro e Escuro, totalizando 12 combinações. Tons escuros usam undertones discretos, e tons claros usam fundos quase brancos tingidos. Não há cálculo arbitrário de opacidade em runtime.

## Aplicação WPF

Os novos `DynamicResource` são atualizados juntos em runtime. A janela principal usa a paleta no background, cabeçalho, cards de prévia/playlist, painel interno da playlist, transporte, status, timeline, volume, operador, hover e seleção. A área real da prévia permanece preta.

Configurações ganhou fundo, sidebar contínua, superfície de conteúdo, tabs selecionadas/hover, cards e divisores tematizados. A prévia de Aparência agora demonstra janela, sidebar, seleção, card secundário, controle e ação principal.

Cores semânticas de erro, aviso e sucesso permanecem independentes do accent.

## Sistema e alto contraste

Sistema continua lendo `AppsUseLightTheme`. O evento de preferência do Windows reaplica o modo e todos os recursos de superfície, sem deixar partes da paleta anterior.

Alto contraste tem prioridade absoluta. Todos os recursos personalizados são substituídos por `SystemColors` de Window, Control, Highlight, HighlightText, WindowText e GrayText.

## Atualização automática preservada

Cada novo processo com verificação automática habilitada continua agendando uma consulta real 1,5 segundo após o carregamento. Cache recente não bloqueia essa consulta. O log registra agendamento, preferência automática, início e conclusão da consulta e o resultado com a versão encontrada.

## Testes

A cobertura verifica as 12 combinações, presença de todos os papéis semânticos, contraste de texto/accent/seleção, diferenças de window/surface/sidebar entre as seis cores, variantes claro/escuro, prioridade do alto contraste e uso estrutural dos recursos na MainWindow e em Configurações.

O pipeline oficial `scripts/build-release.ps1` concluiu com sucesso:

- `dotnet restore` e `dotnet format --verify-no-changes`: aprovados;
- build Debug e Release: 0 erros e 0 warnings;
- testes Debug: 209/209 aprovados;
- testes Release: 209/209 aprovados;
- stress em dez repetições: 40/40 cenários críticos anteriores e 100/100 testes de paleta, total 140/140;
- ZIP: `GuiaPlay-0.10.1-prototipo-win-x64.zip` (240.098.153 bytes);
- Setup: `GuiaPlay-Setup-0.10.1-prototipo.exe` (151.146.888 bytes).

SHA-256 conferido independentemente após a montagem:

- ZIP: `aa7faf96fe0be4b93816f9875dbb8c83bbed10d5594d689b65ebdc9f0190a7c9`;
- Setup: `2b957643201e2343b9aa5727194799caab0f3eda76197284561ed001b5fdbce7`.

O ZIP contém 1.667 entradas e `GuiaPlay.exe`; o executável informa `ProductVersion` **0.10.1-prototipo** e `FileVersion` **0.10.1.0**.

A inspeção automatizada da janela WPF não estava disponível porque o controlador expôs somente navegador, sem aplicativos nativos; a validação física de cor e DPI permanece explicitamente para Andrew.
