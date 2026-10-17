# GP-I11 - Diagnostico de encerramento inesperado na inicializacao (pendente)

Data: 17/10/2026. Branch: `feat/gp-interface-3-modelo1`.

## Evidencia real informada pelo operador

- Comando: `dotnet run --project .\src\GuiaPlay.App\GuiaPlay.App.csproj -c Debug`.
- Sintoma: a janela aparece brevemente, o cursor fica ocupado e a janela some.
- Exit code: **-1073741819**, equivalente ao status Windows **0xC0000005 / STATUS_ACCESS_VIOLATION**.
- As ultimas linhas do log compartilhado eram de 16/10/2026 23:40 e se referiam a versao 1.0.3, inclusive consulta inicial ao GitHub.
- **Sem evento Windows Application Error 1000 e nome da DLL culpada, nao existe prova de causa raiz.** Nao presumir falha do WebView2, do LibVLC, do monitor ou do atualizador apenas pela ultima linha de log. Uma excecao de acesso nativo pode terminar o processo sem disparar os handlers .NET da aplicacao.
- O log padrao e compartilhado com a instalacao estavel e pode incluir registros de executaveis diferentes.

## Instrumentacao sem alterar o aplicativo

Adicionado `tools/gp-i11-startup-diagnostics.ps1`, validado sintaticamente no CI Windows PowerShell 5.1. Ele apenas:
1. Verifica instancias `GuiaPlay.exe` que poderiam afetar o diagnostico.
2. Com `-AnalyzeOnly`, coleta eventos Windows 1000/1001/1026 das ultimas 12 horas sem executar o aplicativo.
3. Sem parametros, executa `dotnet run` Debug e coleta os eventos de falha correspondentes.
4. Mostra exit code, nome do modulo de falha (quando presente), data/hora e 16 ultimas linhas do log.
5. Guarda relatorio local em `artifacts/gp-i11-startup-diagnostics/crash-report.txt`.

**Nao** cria instaladores, dumps, chaves de registro, publica Releases ou envia dados. Recomendar ao usuario ocultar nomes de usuario, caminhos e informacoes privadas antes de compartilhar o relatorio.

## Comandos

```powershell
git pull --ff-only origin feat/gp-interface-3-modelo1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\gp-i11-startup-diagnostics.ps1 -AnalyzeOnly
```

Se os eventos anteriores nao permitirem identificar o modulo:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\gp-i11-startup-diagnostics.ps1
```

**Gate:** aguardar modulo de falha, caminho e horario confirmados; so entao implementar correcao ciblada. Nao declarar o problema resolvido ate reproduzir app estavel na maquina do operador.


## Evidencias adicionais recebidas do operador em 17/10/2026

O relatorio `crash-report.txt` compartilhado comprova:
- Tres falhas do binario `src/GuiaPlay.App/bin/Debug/net10.0-windows/win-x64/GuiaPlay.exe` com `0xC0000005`, modulo reportado como `ntdll.dll` (16/10, 23:31:50, 23:40:50, 23:45:17).
- Uma falha anterior (16/10, 23:31:26) aponta diretamente `libvlc/win-x64/libvlc.dll`, versao 3.0.24.0. **LibVLC e suspeito concreto** na inicializacao; `ntdll.dll` isoladamente nao prova que seja o modulo causador.
- `dotnet run` terminou com o mesmo exit code -1073741819 (0xC0000005).
- Os registros de log 1.0.3 continuam escritos em arquivo compartilhado e nao fornecem uma stack nativa completa. Eventos WER apenas mostram DLL de falha e codigo, nao a causa raiz.

## Correcao de isolamento aplicada (ainda sem teste fisico)

Commit `bfaddc2a0a73b745cc1f1a5242d48a45587b740b`:
- Motor `LibVlcPlaybackEngine` inicializado **sob demanda**, durante primeira carga de arquivo local ou acesso explicito a recursos do motor; nao mais no construtor da janela.
- Atualizacao periodica de monitores nao enumera dispositivos LibVLC antes de haver motor.
- Slider de volume nao constroi LibVLC como efeito colateral do carregamento XAML.
- Encerramento nao tenta inicializar LibVLC apenas para descartar um motor que nunca foi criado.
- Callback de video/audio do LibVLC continua ligado na construcao sob demanda, com registros locais `GP-I11` delimitando o inicio/fim dessa inicializacao.
- Reproducao YouTube/GUI inicial permanecem disponiveis mesmo antes do LibVLC local; **nenhum ajuste de pasta, licença ou instalador**.

**Hipotese testavel:** se a interface permanecer aberta sem arquivo local, o problema envolve o inicio antecipado do LibVLC ou seu probing de audio nativo. Se ainda fechar no mesmo ponto, comparar evento WER novo com os anteriores para outra investigacao. Abrir e carregar arquivo local separadamente para determinar se a falha so ocorre ao usar LibVLC. Isto e isolamento preventivo, **nao correcao causal comprovada**.

## Proximo teste fisico

```powershell
git pull --ff-only origin feat/gp-interface-3-modelo1
dotnet run --project .\src\GuiaPlay.App\GuiaPlay.App.csproj -c Debug
```

Se fechar, executar `tools/gp-i11-startup-diagnostics.ps1 -AnalyzeOnly` e enviar o novo evento de falha e as linhas `GP-I11` do log, sem dados pessoais.

## GP-I11.2 — nova falha, reparacao por evidencia (17/10/2026)

**Observacao confirmada:** a falha de 17/10/2026 07:21 (PID 0x54AC) ocorreu com modulo `ntdll.dll`, `0xC0000005`, identico bucket WER. Desta vez o log **NAO** registrou `GP-I11: Inicializando LibVLC sob demanda`. Conclusao: a inicializacao antecipada do LibVLC era um defeito de arquitetura corrigido, mas **nao era explicacao suficiente para o crash restante**. Nao mascarar esse fato e nao anunciar a falha como resolvida.

### Investigacoes realizadas no codigo

- Revisao dos P/Invokes de monitores em `MonitorService.cs` (`GetDisplayConfigBufferSizes`, `QueryDisplayConfig`, `DisplayConfigGetDeviceInfo`, `EnumDisplayMonitors`, `GetMonitorInfo`, `GetDpiForMonitor`).
- Analise do background timer de monitor e consulta automatica de atualizacao GitHub, cujas execucoes coincidiam com o periodo da falha.
- O evento WER nao contem a stack nativa; a DLL `ntdll.dll` pode ser o ponto de deteccao de corrupcao originada antes. E necessario isolar o componente ou capturar dump.

### Alteracoes feitas, SEM alterar o comportamento Release

1. `DevelopmentDiagnostics.cs`: tres gates **exclusivos da compilacao Debug** (leitura de variaveis de ambiente). Nao altera verificacao de licenca nem sua persistencia:
   - `GUIAPLAY_DIAG_SKIP_DISPLAYCONFIG`: a enumeracao Win32 de monitores continua, mas a identidade persistente via DisplayConfig nativo e omitida; nenhuma identificacao e falsamente considerada confiavel.
   - `GUIAPLAY_DIAG_SKIP_MONITOR_POLL`: interrompe somente o polling periodico de topologia, mas mantem enumeracao inicial.
   - `GUIAPLAY_DIAG_SKIP_STARTUP_UPDATE`: suspende apenas a consulta automatica de atualizacao no inicio, sem desativar atualizacoes na versao Release.
2. Pontos de instrumentacao detalhados da sequencia de enumeracao, polling e atualizacao.
3. Logger inicial registra PID e caminho do executavel, para evitar confundir diagnostico de Debug com os logs da instalacao estavel.
4. `tools/gp-i11-isolate-native-crash.ps1`: **um unico comando** compila o projeto Debug e executa quatro experiencias supervisionadas em sequencia, aguardando por um intervalo e fechando somente a instancia que o proprio script iniciou. Nao altera registro, arquivos de licencia, pasta do usuario, instaladores ou Release. `artifacts/gp-i11-isolation/isolation-result.txt`.
5. `tools/gp-i11-native-dump.ps1`: opcao de obtencao de uma **stack nativa real** com ProcDump oficial da Microsoft (assinatura validada); download somente se `-DownloadOfficial` for explicitamente passado; a EULA nao e aceita automaticamente. Dump local nao e carregado/compartilhado pelo script. Se `cdb.exe` do Windows Debugger estiver presente, gera `native-stack.txt`. Nao enviar dump sem revisao: pode conter memoria de licenca e arquivos privados.

### Protocolo de isolamento

```powershell
git pull --ff-only origin feat/gp-interface-3-modelo1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\gp-i11-isolate-native-crash.ps1
```

Se a matriz nao identificar um componente especifico, capturar stack:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\gp-i11-native-dump.ps1 -DownloadOfficial
```

**Proibido:** mexer em `main`, restaurar/remover licencia, apagar dados, desabilitar protecoes de acesso comercial, executar instalador, publicar release ou atribuir causa raiz sem stack/isolamento.

**Status:** identificacao da causa nativa **pendente da execucao da matriz na maquina com o crash**. A correção final deve ser proposta como alteracao do componente confirmado e revalidada fisicamente.
