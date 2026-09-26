# Relatório M09 — Robustez, Performance e Validação Prolongada

Versão: **0.9.0-prototipo**

Data da versão: **26/09/2026**

Canal: **Prototype**

## Escopo e decisões

O M09 não adiciona um segundo player, serviço residente, banco ou automação gráfica. A arquitetura continua com uma sessão LibVLC, um relógio e um `WriteableBitmap` compartilhado pela prévia e pelas saídas. As mudanças se concentram em ciclo de vida, backpressure, observabilidade, concorrência e falhas recuperáveis. O M10 não foi iniciado.

Não foram inventados números de GPU nem resultados de reprodução prolongada. GPU continua sendo observada externamente e os ensaios físicos de 30 minutos, 1 hora e 2 horas permanecem para Andrew.

## Ciclo de vida do LibVLC

Cada `Session` mantém `MediaPlayer`, `Media`, callbacks e `FrameBufferRing`. Ao trocar mídia ou saída de áudio, a referência corrente é invalidada antes do stop/dispose. Eventos nativos só são publicados quando a sessão que os originou ainda é exatamente a sessão corrente; isso cobre o caso em que duas sessões diferentes compartilham a mesma geração durante reconfiguração de áudio.

Os handlers são removidos antes do stop final. Falhas parciais no construtor liberam player, mídia e buffers já criados. Load, reconfiguração, Stop e Dispose são serializados pelo mesmo gate, com nova verificação de dispose depois da espera. O semáforo não é descartado enquanto chamadas concorrentes ainda podem estar aguardando.

## Pipeline e backpressure

O LibVLC escreve RV32 em três slots nativos alinhados. Um slot pronto antigo pode ser reutilizado e `TryAcquireLatest` libera frames prontos anteriores, mantendo o mais recente. No WPF, `_renderPending` limita a uma operação de renderização pendente; callbacks adicionais atualizam somente a geração/frame mais recente. Assim não há fila de bitmaps ou backlog ilimitado.

São contados frames recebidos, renderizados e não apresentados, além do tempo médio da cópia `WritePixels`. A aplicação continua fazendo uma cópia CPU por frame apresentado; não foi feito rewrite de GPU sem evidência física que justificasse o risco.

## Diagnóstico

A guia **Diagnóstico** mostra e copia:

- versão, Windows e uptime;
- Working Set, memória privada e memória gerenciada;
- coleções GC 0/1/2 e CPU aproximada do processo;
- estado, nome do arquivo sem diretório, tipo de mídia e saídas ativas;
- frames recebidos/renderizados/substituídos, cópia média e trocas de mídia;
- estado conhecido do update.

A primeira amostra de CPU aparece como “coletando”; as seguintes usam a diferença de tempo de CPU dividida pelo intervalo e pelos processadores lógicos. A área atualiza a cada dois segundos somente enquanto Configurações está aberta. O relatório não inclui caminho completo, conteúdo, token nem GPU.

Snapshots compactos entram no log ao carregar, iniciar, parar, finalizar, falhar e fechar. A rotação permanece em 5 MiB e no máximo cinco arquivos, agora apoiada por componente isolado e testado.

## Concorrência, dispositivos e arquivos

O Named Pipe separa recepção e processamento: conexões são aceitas sem lançar handlers paralelos, e as solicitações seguem por uma fila de leitor único. Exceções de handler são registradas sem matar o listener.

Rajadas de `WM_DISPLAYCHANGE` são coalescidas por 500 ms para evitar repetição de reposicionamento e MessageBoxes. As políticas existentes continuam: perda de todas as saídas de vídeo pausa; perda do operador ou áudio obrigatório exige reconfiguração; áudio nunca cria saída visual.

Existência de mídia é consultada fora do Dispatcher e recebe timeout de quatro segundos. Arquivo ausente, USB removido e rede indisponível mantêm a referência da playlist e deixam o aplicativo utilizável. O conteúdo continua sendo aberto diretamente da origem, sem cópia. Conteúdo vazio/truncado ou falso codec é rejeitado pelo LibVLC com estado inline e log contextual.

## Stop, fim natural e encerramento

**Stop** fecha as saídas, para o player, volta a posição para zero e mantém a mídia carregada; Play pode iniciar novamente. O fim natural fecha apenas janelas externas, mantém o GuiaPlay aberto, conserva a mídia e habilita “Reproduzir novamente”. Eventos atrasados depois de Stop/fim não transformam o estado em erro.

Ao fechar, timers e janelas auxiliares são encerrados, um snapshot é registrado e o engine é descartado. Download de update em andamento é cancelável e o arquivo parcial é apagado; aplicação/restart continua bloqueada durante playback.

Exceções recuperáveis de I/O na UI são registradas e tratadas. Uma exceção desconhecida não é marcada como recuperada: é registrada, informada e encerra o processo de forma controlada. Exceções não observadas e de AppDomain também são registradas.

## Persistência e volume

`settings.json` e `playlist.json` continuam usando arquivo temporário e replace no mesmo volume. Testes cobrem ausência, vazio, truncamento, JSON inválido, falha de escrita/replace e preservação do arquivo válido anterior. Volume é limitado a 0..100 e alterar o slider enquanto mudo preserva o novo valor para o unmute.

## Testes automatizados e performance controlada

A cobertura M09 inclui todas as categorias de callback antigo, 100 trocas lógicas, Stop/fim/erro, volume/mute, timeline, áudio sem saídas, probe de arquivo, persistência corrompida, rotação, diagnóstico/redaction, cancelamento de update, 12 execuções concorrentes via Named Pipe e políticas de desconexão.

O cenário de playlist cria, move, serializa e recarrega 1.000 referências sem mídias reais e usa limite generoso de 15 segundos apenas para detectar regressão absurda. O script `scripts/soak-test.ps1` coleta CSV de CPU, Working Set, memória privada, handles, threads e responsividade a cada cinco segundos por padrão.

O grupo de stress — 100 trocas lógicas, 12 execuções concorrentes simuladas e playlist de 1.000 itens — foi executado dez vezes consecutivas em Debug: 30/30 casos aprovados, sem timeout ou flakiness observada. Isso valida a lógica e o IPC no ambiente de desenvolvimento; não substitui reprodução prolongada com LibVLC e hardware real.

## Resultado da montagem

O pipeline oficial `scripts/build-release.ps1` concluiu com sucesso:

- `dotnet format --verify-no-changes`: aprovado;
- build Debug e Release: 0 erros e 0 warnings;
- testes Debug: 182/182 aprovados;
- testes Release: 182/182 aprovados;
- ZIP: `GuiaPlay-0.9.0-prototipo-win-x64.zip` (240.062.909 bytes);
- Setup: `GuiaPlay-Setup-0.9.0-prototipo.exe` (151.124.599 bytes).

SHA-256 conferido independentemente após a montagem:

- ZIP: `6e1d91b2590e925cf5fe12fc42922bdbce8d4d8eab3ed4545c666fd40844a2eb`;
- Setup: `3a385fff8a042320ffa5071a1ee49a3c0b53edd8ba158e78d4dc8b58473fd2bd`.

O ZIP contém 1.667 entradas, incluindo `GuiaPlay.exe` e `GuiaPlay.Updater.exe`; o executável publicado informa `ProductVersion` **0.9.0-prototipo** e `FileVersion` **0.9.0.0**. A publicação e a validação do update real no GitHub são registradas depois da criação da prerelease.
