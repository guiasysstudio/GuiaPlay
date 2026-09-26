# Validação de performance e longa duração — GuiaPlay 0.9

Este roteiro separa medições automatizadas leves de validação física. Não conclua que 4K, um codec ou múltiplas saídas são adequados sem medir no computador real do culto.

## Coleta interna

Abra **Configurações > Diagnóstico** para ver um snapshot a cada dois segundos. Use **Copiar diagnóstico** no início, em pontos intermediários e no final. O resumo omite o diretório da mídia e não contém tokens ou conteúdo.

CPU é aproximada pelo processo. GPU não é coletada pelo GuiaPlay: use Gerenciador de Tarefas, Performance Monitor ou ferramenta do fabricante.

## Soak test assistido

Com um publish já montado:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\soak-test.ps1 -Launch -DurationMinutes 30
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\soak-test.ps1 -DurationMinutes 60
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\soak-test.ps1 -DurationMinutes 120
```

O primeiro comando pode abrir o GuiaPlay. Sem `-Launch`, o script monitora a instância mais recente já aberta. O CSV vai para `artifacts\soak` e não deve ser versionado. Durante o ensaio, Andrew inicia a mídia e executa as trocas/seeks manualmente. `-CloseWhenDone` solicita fechamento normal somente para uma instância iniciada pelo próprio script; nunca usa encerramento forçado.

Registrar no mínimo:

- diagnóstico no início e no fim;
- RAM sem crescimento indefinido após aquecimento;
- CPU média/pico aproximados;
- responsividade do painel e das saídas;
- erros/logs, frames recebidos/renderizados/substituídos;
- conexão, resolução, escala, codec e quantidade de saídas.

## Matriz de formatos

| Categoria | Contêiner/codec mínimo | Arquivo usado | Resultado | Observações |
|---|---|---|---|---|
| Vídeo | MP4 / H.264 |  |  |  |
| Vídeo | MKV |  |  |  |
| Vídeo | AVI |  |  |  |
| Vídeo | MOV |  |  |  |
| Vídeo | WebM |  |  |  |
| Vídeo | MPEG |  |  |  |
| Vídeo | TS/M2TS |  |  |  |
| Áudio | MP3 |  |  |  |
| Áudio | WAV |  |  |  |
| Áudio | OGG |  |  |  |
| Áudio | FLAC |  |  |  |
| Áudio | AAC/M4A |  |  |  |
| Áudio | WMA |  |  |  |
| Áudio | OPUS |  |  |  |

## Tabela de resultados físicos

| Resolução | Codec | Duração | Saídas | CPU | RAM inicial | RAM final | GPU observada externamente | Falhas | Observações |
|---|---|---:|---:|---:|---:|---:|---:|---|---|
| 720p |  | 30 min | 1 |  |  |  |  |  |  |
| 1080p | H.264 | 1 h | 1 |  |  |  |  |  |  |
| 1080p | H.264 | 1 h | 2 |  |  |  |  |  |  |
| 4K |  | 30 min | 1 |  |  |  |  |  |  |
| Sequencial | Vários | 2 h |  |  |  |  |  |  |  |

## Cenários de falha durante o soak

1. Executar 30 ou mais trocas; usar 100 quando houver mídia/harness adequado.
2. Fazer seeks rápidos no início, meio e fim, tocando e pausado.
3. Alternar play/pause/play/stop e reproduzir novamente.
4. Remover e reconectar HDMI; confirmar pausa somente quando nenhuma saída válida restar.
5. Desconectar áudio explícito; confirmar pausa sem fallback/retomada automática.
6. Remover USB depois de adicionar a referência à playlist.
7. Indisponibilizar caminho UNC e confirmar timeout/erro controlado.
8. Tentar arquivo vazio, truncado e extensão válida com conteúdo inválido.
9. Abrir playlist com 100, 500 e 1.000 referências.
10. Verificar e baixar update durante playback; não aplicar até estado seguro.
11. Fechar durante vídeo, áudio, pausa e download cancelável.

## Critério de relato

Anotar somente valores efetivamente observados. “Sem travamento” não significa “4K validado”; registrar hardware, resolução, codec, duração e número de saídas. Se houver crescimento contínuo, guardar o CSV e os logs, sem adicionar mídia ou dados pessoais ao repositório.
