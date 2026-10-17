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
