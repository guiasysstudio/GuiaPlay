# Relatório de implementação — M01

Data: 25/09/2026  
Versão: 0.1.0-prototipo

## Ambiente usado

- Windows x64, build 10.0.26200.
- Processo e RID x64 (`win-x64`).
- .NET SDK 10.0.300; runtime `Microsoft.WindowsDesktop.App` 10.0.8.
- Nenhuma carga de trabalho adicional foi necessária.
- Pasta inicial estava vazia, sem `AGENTS.md` aplicável e sem repositório Git; nenhum outro projeto foi alterado.

## Entregue

- Solução `GuiaPlay.slnx` com `GuiaPlay.App`, `GuiaPlay.Core` e `GuiaPlay.Core.Tests`.
- Painel WPF em português, enumeração/identificação de monitores e janelas sem bordas.
- Reprodução LibVLC incorporada com uma fonte/relógio e distribuição real dos quadros.
- Controles, seek, confirmação antes de tocar, áudio único, fim/parada/pausa/troca e desconexão.
- Gerações de sessão, despacho para UI, buffers limitados, encerramento assíncrono e logs rotativos.

## Validação automatizada

Os testes unitários cobrem:

- fim natural fecha saídas sem descarregar o arquivo/painel;
- parar fecha saídas e permite repetir;
- pausar não fecha saídas;
- cancelamento da troca preserva mídia/estado;
- evento antigo não encerra mídia nova;
- perda parcial mantém reprodução e perda total pausa.

Resultados executados nesta máquina:

- `dotnet restore .\GuiaPlay.slnx --force-evaluate`: aprovado para os três projetos;
- build Debug: aprovado, zero avisos e zero erros;
- testes Debug: **7/7 aprovados**;
- build Release: aprovado, zero avisos e zero erros;
- testes Release: **7/7 aprovados**;
- `dotnet format --verify-no-changes`: aprovado, 0 de 24 arquivos necessitavam formatação;
- versões restauradas coincidem com as solicitadas no projeto.

Smoke de inicialização Debug: o executável x64 foi iniciado por quatro segundos, permaneceu ativo e responsivo, carregou as dependências nativas e registrou `GuiaPlay 0.1.0-prototipo iniciado`; em seguida foi encerrado pelo PID exato do teste. O ambiente enumerou três telas (`1920×1080 @ X=0`, `1440×900 @ X=1920` e `1440×900 @ X=-1440`), incluindo portanto coordenada negativa. A automação visual nativa não estava disponível, então esse smoke não valida layout nem reprodução.

## Validação que exige hardware/mídia

Pendente no computador da igreja:

- operador + duas saídas físicas;
- áudio real, latência comparada e desconexão por cabo;
- 1080p com movimento/timecode e medição prolongada;
- 4K e mais de duas saídas (não validados nem prometidos);
- matriz de codecs/arquivos inválidos.

Não foi procurado vídeo indiscriminadamente no computador. Sem arquivo de teste explicitamente fornecido, o smoke test de mídia real fica pendente.
