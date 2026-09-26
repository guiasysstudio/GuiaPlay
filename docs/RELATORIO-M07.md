# Relatório M07 — Polimento Operacional e Atualizações 2.0

Versão: **0.7.0-prototipo**

Data da versão: **26/09/2026**

Canal: **Prototype**

## Correção do update automático

O defeito físico vinha da combinação entre `LastUpdateCheckUtc` persistido e `UpdateManager.LastResult` exclusivamente em memória. Um processo novo respeitava as 12 horas, recebia resultado nulo e não tinha como reconstruir a seta.

O schema 5 agora persiste a versão do produto que fez a consulta, versão/data conhecida e status. O `UpdateManager` restaura esse estado na criação e a janela principal aplica o indicador no startup, antes de depender de Configurações. O cache só é válido se pertencer exatamente à versão instalada e for semanticamente coerente. Ausência de estado, versão diferente, timestamp inválido, falha anterior ou combinação incoerente força nova consulta automática quando habilitada. A busca manual sempre ignora o intervalo.

O cache contém somente metadados leves. Notas completas, manifesto e URLs do pacote não são persistidos; quando uma instalação parte de estado restaurado, o gerenciador atualiza os dados online antes do download.

## Download e preparação

`UpdatePackageDownloader` copia o stream assincronamente, contabiliza bytes reais e limita notificações a aproximadamente dez por segundo. Com `Content-Length`, a interface mostra total e percentual; sem tamanho, mostra bytes recebidos e barra indeterminada. A preparação diferencia download, verificação SHA-256, extração/preparação e início do updater. Percentual é usado somente no download.

O usuário pode cancelar enquanto o pacote está sendo baixado. Arquivos parciais e a pasta temporária são removidos. O comando desaparece antes de SHA, extração ou aplicação, preservando integridade e rollback. O cliente de download não usa o timeout curto das consultas ao GitHub, evitando abortar pacotes grandes em conexões legítimas.

## Operação de mídia e interface

- roda sobre volume: passos centralizados de 5 pontos, clamp 0..100, persistência já existente e política de mudo preservada;
- roda sobre timeline: passos de 5 segundos, clamp por duração e labels imediatos;
- clique direto na timeline: cálculo proporcional em DIPs, com clamp e sem duplicar clique do thumb;
- página Sobre: URL textual removida; o botão permanece e usa `ProductInfo.ProjectPageUri`;
- wordmark M06 preservado sem redesenho.

## Instalador

O Inno Setup mantém branding, português do Brasil, instalação por usuário, ausência de elevação, atalhos e preservação dos dados. A entrada `[Run]` agora usa `unchecked`: **Executar GuiaPlay** continua disponível na última página, mas começa desmarcado. A tarefa do atalho da Área de Trabalho continua opcional e desmarcada.

## Qualidade

A cobertura automatizada inclui agenda/cache por versão, restauração do indicador, persistência após reinício simulado, busca manual, progresso em bytes, matemática de volume/timeline e flags do instalador. Testes WPF frágeis por pixel não foram adicionados; os cálculos foram isolados no Core.

Resultados antes da publicação:

- `dotnet format --verify-no-changes`: aprovado;
- build Debug e Release: 0 erros e 0 warnings;
- testes Debug e Release: 133/133 aprovados em cada configuração;
- ZIP: `3b65296a0b8864be1bc74107a1b4c3abd07f8e073be14c8837e0d96291657125`;
- Setup: `67f397a5a068875d87fb2b82a65fc4aa4e091804c8d67d4806dcebe3e42034d7`;
- manifesto conferido contra o ZIP e entradas essenciais do pacote validadas.

As consultas reais contra a prerelease são executadas após a publicação. A validação física em hardware continua no roteiro manual. Nenhum item do M08 foi iniciado.
