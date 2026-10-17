# GP-I03 — Tabela de mídias profissional (Modelo 1)

Data: 16/10/2026  
Branch: `feat/gp-interface-3-modelo1`  
Situação: **implementada no código; compilação, testes automatizados e aceite físico adiados pelo responsável até GP-I10**.

## Escopo implementado

- Centro do workspace agora contém a prévia do operador acima da tabela de mídias de reprodução, com linha de ações e cabeçalhos.
- Colunas visuais: `#` (ordem global real), `Mídia` (miniatura disponível, ícone de tipo, nome e status), `Duração`, `Tipo`, `Resolução`, `Data`, `Grupo` e `Ações`.
- Metadados de duração e resolução vêm de `LibVLCSharp` via `Media.Parse(MediaParseOptions.ParseLocal, timeout: 1500)` e `Media.Tracks`; data é a última gravação informada pelo sistema de arquivos. Em caso de informação ausente ou falha, exibe `—`, sem inventar propriedades.
- A miniatura é um thumbnail real fornecido por `IShellItemImageFactory` do Windows, quando disponível; ao falhar, apresenta ícone de vídeo/áudio em vez de imagem simulada. `DeleteObject` e `ReleaseComObject` liberam recursos nativos.
- O trabalho de metadados está fora do Dispatcher/UI, com lote limitado aos primeiros 150 itens, prazo de parse e cancelamento ao trocar playlist/encerrar. Caminhos UNC não são analisados automaticamente pelo serviço visual; o mecanismo existente de reprodução/probe continua preservado.
- Busca por nome, grupo, tipo e caminho com filtro em `ICollectionView`; não muda posição nem conteúdo armazenado. Resultado/total e estado vazio visíveis.
- Duplo clique em mídia e opção `Reproduzir` no menu da linha chamam a mesma política existente de ativação: respeitam disponibilidade, monitor público obrigatório para vídeo, áudio independente, troca confirmada e licenciamento.
- Menu `⋯`: `Reproduzir`, `Mover para cima`, `Mover para baixo` e `Remover referência`. O menu desativa ações inaplicáveis.
- Setas superiores permitem subir/descer mídia dentro do grupo. Arrastar uma linha para outra muda ordem e, conforme o destino, grupo; usa `PlaylistCatalog.MoveItem` e persiste no `playlist.json` existente.
- Ordenação livre das colunas está intencionalmente desabilitada: o símbolo `#` sempre significa ordem operacional, sem divergência entre interface e execução.
- A grade e seus botões ficam dentro de `LicensedWorkspace`, compartilhando o bloqueio por licença. A seleção é mantida após salvar e reconstruir a tabela usando ID estável do item.
- Cores, hover, seleção, cabeçalho, foco, contraste e scroll seguem os brushes da identidade atual; textos das linhas e feedback refletem dados reais.

## Arquivos de código

1. `src/GuiaPlay.App/MainWindow.xaml` — grade central, busca, setas e estilo.
2. `src/GuiaPlay.App/MainWindow.xaml.cs` — handlers, persistência, ordenação, filtro, menu, carregamento, seleção e integração com a árvore.
3. `src/GuiaPlay.App/Models/MediaGridRow.cs` — view model ligado a grupos e itens reais.
4. `src/GuiaPlay.App/Services/MediaGridMetadataService.cs` — propriedades de arquivos, LibVLC local e thumbnails do Windows.
5. `tests/GuiaPlay.App.Tests/MediaGridRowTests.cs` — testes escritos para identidade, posição, atualização de metadados e arquivo ausente (**não executados ainda**).
6. `docs/PLANO-INTERFACE-3-MODELO1.md` — progresso.

## Revisão estática

- 618 tags XAML verificadas quanto a fechamento e nesting; sem nomes `x:Name` duplicados; handlers referenciados na janela foram encontrados no code-behind.
- Recursos locais do tema encontrados; `SystemFillColorCriticalBrush` já era uma referência de sistema presente anteriormente no XAML da aplicação.
- APIs de LibVLCSharp (`Media.Parse`, `Media.Tracks`, `Media.Duration`) conferidas pela documentação técnica do próprio LibVLCSharp 3.x.
- **Não há afirmação de build/execução/testes aprovados**. A validação final conjunta ocorrerá após GP-I10, incluindo x86/x64, extração de miniaturas em formatos reais, 1.000 itens e janela 1366×768 com 100/125/150% DPI.

## Limites e próximo passo

- A API Shell não fornece miniatura de todo formato. Algumas mídias aparecerão com fallback verdadeiro de tipo.
- A primeira varredura cobre no máximo 150 linhas para preservar consumo de recursos; demais linhas ficam com tipo/nome e `—` até uma evolução de carregamento sob demanda se necessária no aceite.
- GP-I04 completará `Adicionar pasta`, descoberta de subpastas, rejeições e duplicados; não há botão de link YouTube falso na interface da GP-I03.
- GP-I05 acrescentará a apresentação da área `Programa / Tela Pública` e a coluna real de áudio; GP-I06 aprimorará timeline/transporte; GP-I07 atalhos, GP-I08/09 YouTube, GP-I10 avaliação do conjunto.

Nenhum instalador, deploy, Release, tag ou alteração de `main` foi feito.
