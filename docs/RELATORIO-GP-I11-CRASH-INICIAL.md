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
