# Backlog por marcos

## M01 — 0.1.0-prototipo

Protótipo funcional multitelas, prévia/controles, motor compartilhado por callbacks, ciclo de vida seguro, testes da lógica, logs e roteiro de validação. Implementado nesta entrega; validação física continua pendente conforme o relatório.

## M01.1 — 0.1.1-prototipo

- Tema Fluent nativo do WPF com opções Sistema, Claro e Escuro.
- Persistência mínima e isolada da aparência, antecipada deliberadamente do M02.
- Correção de contraste de controles, acabamento responsivo e rótulos curtos de monitores.
- Identificadores compactos no canto inferior esquerdo da área útil, sem cobertura da tela.
- O retorno manual de Andrew informa que a reprodução do M01 funciona normalmente nas telas disponíveis; esse relato não equivale a medição de desempenho ou prova de sincronismo físico perfeito.

## M02 — 0.2.0-prototipo

- Implementado armazenamento versionado e atômico no `settings.json` existente para aparência, telas, operador, pré-seleção pública, áudio, volume e mudo.
- Implementados nomes personalizados, configuração Salvar/Cancelar e identidade persistente por caminho de dispositivo do Windows, sem restauração automática ambígua.
- Implementada seleção real de saída pelo par módulo/dispositivo aceito pelo LibVLC, incluindo Padrão do Windows, estado indisponível sem fallback e pausa em perda detectada.
- Implementadas regras de operador provisório, desconexão/reconexão sem reautorização e reposicionamento do painel na área útil/DPI da tela.
- Acrescentados testes de migração, falhas, reconciliação de telas, identidade de áudio e eventos antigos.
- Diagnóstico amigável de desempenho e eventual pipeline GPU/plugin nativo continuam dependentes dos dados de validação prolongada no computador alvo; não foram ampliados neste marco.

## M03 — 0.3.0-prototipo

- Interface focada na operação ao vivo, com configurações permanentes na engrenagem e controles compactos na faixa inferior.
- Playlist/cronograma com grupos virtuais, referências a arquivos, ordem persistida e sinalização de original ausente.
- Suporte operacional a vídeo e áudio comuns do LibVLC, sem cópia/importação de mídia.
- Política central: vídeo exige saída pública; áudio independe de telas e nunca cria/ativa saída de vídeo.
- Reprodução imediata sem confirmação intermediária e duplo clique na playlist seguindo a mesma política.
- Persistência separada e atômica da playlist, schema 3 de configurações e cobertura automatizada ampliada.

## M04 — 0.4.0-prototipo

- Correções visuais do diálogo de grupo e indicador estático do operador.
- Ícones locais Microsoft Fluent System Icons, sem biblioteca de renderização.
- Arrastar referências do Explorer e reordenar grupos/itens, inclusive entre grupos.
- Argumento de linha de comando e instância única com Mutex + Named Pipe.

## M05 — 0.5.0-prototipo

- Concluído: GitHub Releases, consulta automática/manual, indicador de update, updater com SHA-256, staging seguro, backup/rollback, publish self-contained, ZIP, Inno Setup e pipeline de release.

## M06 — 0.6.0-prototipo

- Concluído: identidade visual oficial, ícone/wordmark, página Sobre, personalização do instalador e primeira atualização pública real.

## M07 — 0.7.0-prototipo

- Concluído: cache persistente e versionado de update, correção da seta no startup, progresso real de download e estágios de preparação.
- Concluído: scroll de volume/timeline, click-to-seek, opção **Executar GuiaPlay** desmarcada por padrão e preservação integral do instalador M06.
- Cobertura automatizada ampliada para cache, agenda, persistência, download, controles de mídia e script do instalador.

## M08 — 0.8.0-prototipo

- Concluído: registro por usuário em **Abrir com**, ProgIDs próprios de vídeo/áudio, `Capabilities`, `RegisteredApplications` e `OpenWithProgids`, sem alterar `UserChoice` ou assumir formatos.
- Concluído: verbo opcional **Abrir com GuiaPlay**, reaproveitando argumento de linha de comando, instância única e Named Pipe, sempre sem autoplay.
- Concluído: tarefas opt-in no Setup, configuração pós-instalação, abertura das Configurações oficiais de aplicativos padrão e limpeza seletiva na desinstalação.
- Concluído: abstração de Registry, testes em memória e round-trip controlado sob subchave HKCU temporária.

## M09 — 0.9.0-prototipo

- Concluído: diagnóstico leve de sessão com memória, CPU aproximada, GC, uptime, playback, saídas, trocas e pipeline de frames, incluindo resumo copiável com redaction de caminhos.
- Concluído: ciclo de vida LibVLC endurecido contra callbacks de sessões substituídas, falhas parciais de criação, corrida com dispose e acúmulo de recursos nativos.
- Concluído: backpressure explícito, métricas pontuais, fila serial do Named Pipe, debounce de mudanças de monitor e probe assíncrono com timeout para mídia local/remota.
- Concluído: rotação testável de logs, cenários de persistência corrompida/falha de replace, playlist com 1.000 itens, download cancelado e stress de trocas/instância única.
- Concluído: `scripts/soak-test.ps1`, matriz de formatos e roteiro de 30 minutos/1 hora/2 horas sem inventar resultados físicos.

## M10 — 0.10.0-prototipo

- Concluído: Interface 2.0 em cartões e Configurações com navegação lateral por Telas, Aparência, Áudio, Atualizações, Windows, Diagnóstico e Sobre.
- Concluído: modos Sistema/Claro/Escuro, seis cores de destaque com variantes e alto contraste prioritário, persistidos no schema 6.
- Concluído: equalizador nativo do LibVLC, desativado por padrão, com descoberta runtime de presets/bandas, preamp, edição personalizada e aplicação segura em tempo real.
- Concluído: consulta real ao GitHub uma vez por novo processo quando habilitada, sem o cache/intervalo de 12 horas suprimir a verificação de startup.
- Preservados: integração Explorer opt-in, instância única, updater, instalador per-user e robustez/diagnóstico M09.

## M10.1 — 0.10.1-prototipo

- Concluído: correção da aplicação visual dos seis accents em toda a identidade da interface, com 12 paletas explícitas para Claro/Escuro.
- Concluído: fundos, cards, sidebar, playlist, transporte, controles, hover, seleção e divisores usam recursos semânticos dinâmicos do GuiaPlay.
- Concluído: alto contraste continua resolvendo todas as superfícies para `SystemColors`; Sistema recalcula tema e paleta quando a preferência do Windows muda.
- Preservados sem mudança de schema: equalizador M10, integração Windows M08 e consulta real de update em cada novo processo.

## Próximo marco — não iniciado

- **1.0.0-rc1 — estabilização final.**
- O release candidate permanece separado desta entrega e não foi criado nem publicado.

## Não planejado para estes marcos

Streaming, YouTube, projeção de letras, contas, nuvem, licenciamento e captura de outras aplicações.
