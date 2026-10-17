# GP-I01 — Estrutura visual Modelo 1

Data: 2026-10-16  
Base: GuiaPlay 1.0.3 Stable (`main` conservada)  
Branch: `feat/gp-interface-3-modelo1`

## Implementações

- Nova janela principal com área de trabalho ampliada para 1450×830 DIP e mínimo de 980×590 DIP, conservando a moldura nativa do Windows e recursos de aparência por tema.
- Cabeçalho: wordmark oficial, versão informada por `ProductInfo.Version`, navegação funcional de **Playlist**, **Mídia**, **Configurações**, **Monitores** e **Sobre**.
- Playlist: foco na árvore atual; Mídia: diálogo real de abertura; Configurações e Monitores: diálogo de preferências existente; Sobre: abre diretamente a guia Sobre do diálogo já existente.
- Ações de navegação para playlist e abertura de mídia respeitam a autorização/licenciamento já existente e os estados de operação ocupada.
- Botão de licença reutiliza `FormatLicenseSummary` e a lógica GSL-v1 já existente; mantém aviso real de atualização e engrenagem.
- Estrutura de três colunas: playlist de trabalho à esquerda, prévia do operador ao centro, seleção **real** de saídas públicas à direita. A nova lista de monitores está associada à mesma `ObservableCollection<MonitorChoice>` da lista operacional de chips existente; seleção e desmarcação continuam usando a lógica de eventos do core.
- Botões reais **Identificar telas** e **Configurar monitores** na coluna direita.
- Nome da mídia em reprodução deslocado do cabeçalho para o cartão de prévia, sem perder o `x:Name=FileNameText` usado pelo code-behind.
- Nenhum arquivo original do usuário, esquema de playlist, protocolo de licença, updater, instalador ou motor LibVLC foi alterado.

## Arquivos alterados

1. `src/GuiaPlay.App/MainWindow.xaml`
2. `src/GuiaPlay.App/MainWindow.xaml.cs`
3. `src/GuiaPlay.App/Windows/ScreenConfigurationWindow.xaml.cs`
4. `docs/PLANO-INTERFACE-3-MODELO1.md` (ordem e regra de testes final)

## Inspeção estática, sem execução

- Conferidos aninhamento de tags XAML, nomes únicos de controles e resoluções de todos os handlers `Click`.
- Conferida referência à guia Sobre.
- **Sem execução de build, testes automatizados ou testes de GUI** neste marco: decisão do responsável é validar as onze etapas em conjunto após GP-I10.
- Sem geração de instalador, ZIP, tag ou GitHub Release; `main` não foi modificada.

## Limites desta etapa

O shell ainda não reproduz todos os painéis ou a tabela da imagem. A biblioteca de playlists/pastas, metadados e thumbnails, layout de Programa (Tela Pública), duplo seek, transporte ampliado, atalhos e YouTube fazem parte de GP-I02 a GP-I09. Até sua implementação, apenas os controles **já funcionais** são exibidos, sem widgets falsos ou dados inventados. O primeiro aceite visual e funcional no Windows está reservado ao final de GP-I10.
