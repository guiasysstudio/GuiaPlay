# GP-I04 — Importação de pastas e subpastas (Interface 3.0)

Data: 16/10/2026
Branch: `feat/gp-interface-3-modelo1`
Status: **código implementado, compilação e validação funcional pendentes para GP-I10**.

## O que foi implementado

- O botão **Adicionar ▾** no painel de grupos oferece **Adicionar arquivo(s)…** e **Adicionar pasta e subpastas…**, ambas operações reais. A opção YouTube será acrescentada no GP-I09 depois da validação de viabilidade de GP-I08, para não expor botões cenográficos.
- `OpenFolderDialog` nativo do WPF (.NET 10) abre seleção de pasta sem instalar dependências adicionais.
- `FolderMediaScanner.ScanAsync` varre diretórios recursivamente fora do Dispatcher, identificando os tipos previstos em `MediaTypeDetector` sem copiar, abrir para reprodução, renomear ou apagar mídia.
- Pastas e subpastas são mapeadas para **grupos virtuais nomeados por caminho relativo** (ex.: `Evento / Louvor / Manhã`). A organização lógica é preservada sem alterar o schema v1 de `PlaylistDocument`, e grupos existentes permanecem intactos.
- Nomes de grupos extensos recebem um sufixo estável para respeitar o limite de 80 caracteres. Grupos com nomes idênticos oriundos de raízes distintas são desambiguados pelo caminho da raiz.
- Importação repetida não adiciona cópias da mesma referência: caminhos locais já existentes no catálogo e duplicados no mesmo lote são ignorados independentemente de caixa.
- Limites para evitar varreduras descontroladas: **3.000 mídias**, **320 diretórios**, **24 níveis**, **12.000 entradas** por importação. Entradas fora do limite ou de leitura indisponível geram aviso para decisão explícita de importar apenas a parte encontrada.
- Links simbólicos, junções e pontos de reanálise são ignorados; diretórios inacessíveis são relatados (máximo de 20 exemplos no log para limitar tamanho).
- Botão real de cancelamento durante a varredura e indicadores de progresso/conclusão/erro. A operação é cancelável antes da persistência; o painel de reprodução ao vivo não é bloqueado pela leitura da pasta.
- A atualização da playlist é preparada em snapshot independente e **salva em `PlaylistStore` antes de adotar a nova versão no catálogo em memória**. Em falha de escrita, playlist anterior é preservada.
- `PlaylistCatalog.ApplyPersistedChanges` preserva a vinculação ao preset ativo e marca corretamente o workspace como alterado (`IsDirty`).
- O arraste de diretórios vindos do Explorer utiliza a mesma rotina de varredura. Arquivos soltos importados por menu/arraste passam por sonda limitada; repetidos são ignorados e lote é persistido antes de alterar o catálogo vivo.
- O estado de licenciamento é conferido antes e depois da varredura; uma alteração da playlist durante leitura cancela a aplicação do resultado obsoleto. Nenhuma mudança no motor LibVLC, GSL-v1, atualização ou configurações de monitores.

## Arquivos do módulo

- `src/GuiaPlay.Core/FolderMediaImport.cs` — scanner limitado, classificação, mapeamento de grupos e mescla não destrutiva.
- `src/GuiaPlay.Core/Playlist.cs` — adoção de documento já persistido com marcação de alterações.
- `src/GuiaPlay.App/MainWindow.xaml` — Adicionar ▾, progresso e cancelar.
- `src/GuiaPlay.App/MainWindow.xaml.cs` — comandos de arquivo/pasta, arraste do Explorer, validações, cancelamento, commit persistente e mensagens.
- `tests/GuiaPlay.Core.Tests/FolderMediaImportTests.cs` — casos escritos para recursão, ordem, grupos relativos, duplicação, nomes longos, colisão de raízes, cancelamento, pasta ausente e preservação de playlist/arquivos. **Não executados.**
- `docs/PLANO-INTERFACE-3-MODELO1.md` — situação GP-I04.

## Revisão de código sem executar testes

- Verificadas **625 tags XAML**, fechamento/aninhamento, nomes de controles, referências a recursos locais e handlers (sem inconsistências encontradas).
- Conferida presença da seleção real de pasta, serviço de varredura fora da UI, checagem de cancelamento, exclusão de reparse points e persistência antes da alteração em memória.
- **Não foram executados `dotnet build`, `dotnet test`, aplicação GUI ou testes físicos**, por decisão expressa de concluir GP-I00 até GP-I10 primeiro.
- Nenhum instalador, tag, Release ou alteração de `main`.

## Limitações conhecidas e validação posterior

- A árvore lateral continua a representar pastas como **grupos virtuais com caminho relativo no nome**; não há vinculação bidirecional contínua com o filesystem nem exclusão/modificação de diretórios físicos.
- Mídias com extensões suportadas são identificadas inicialmente pelo formato do nome; problemas reais de decodificação são tratados no fluxo já existente de carregamento/reprodução.
- Mídias localizadas além dos limites definidos requerem nova seleção/importação por lotes; não ocorre importação silenciosa de um subconjunto se a varredura foi parcial.
- Recomendado no GP-I10: smoke com muitas subpastas, pastas vazias, arquivos duplicados, caracteres especiais, mídia em rede, falta de permissão, cancelamento, reimportação, arraste misto de arquivos/pastas, preset ativo e perda de licença durante leitura.
- GP-I05: telas públicas, monitores e áudio; GP-I06: transporte; GP-I07: teclado; GP-I08 e GP-I09: viabilidade e integração YouTube; GP-I10: verificação física completa.
