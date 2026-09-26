# Roteiro manual — GuiaPlay M09

## Preparação

- Windows 10 ou 11 x64 com área de trabalho estendida.
- Monitor do operador e, idealmente, duas saídas públicas.
- Vídeos MP4, MKV e WebM conhecidos, com áudio e movimento.
- Áudios MP3, OGG e FLAC conhecidos.
- Um arquivo de teste que possa ser movido temporariamente para validar item indisponível.
- Registrar resolução/escala das telas, conexão, codecs e uso aproximado de CPU/GPU/memória.

## Checkpoint visual obrigatório

1. Abrir **Grupo** em 100%, 125% e 150% de escala: campo, Cancelar e OK devem ficar inteiros, com margens internas e sem corte.
2. Confirmar foco inicial no nome, OK desabilitado com nome vazio, Enter para confirmar um nome válido e Esc para cancelar.
3. Repetir **Grupo** e **Renomear** nos temas Claro e Escuro e em 1366 × 768.
4. Conferir que **Operador** na faixa inferior é um badge estático: sem seta, popup, foco de seleção ou aparência clicável.
5. Desconectar o operador salvo e confirmar aviso discreto no mesmo badge, sem quebrar a faixa inferior.
6. Conferir os ícones Fluent em configurações, grupo, adicionar, renomear, excluir, reproduzir, pausar, parar, volume e mudo, inclusive hover/foco/desabilitado.
7. Redimensionar a janela e conferir alinhamentos, truncamento, playlist e controles em 1366 × 768.

## Arraste e reordenação

1. Arrastar do Explorer um vídeo para um grupo e confirmar que somente a referência foi adicionada.
2. Arrastar simultaneamente vídeo, áudio e `.txt`; aceitar os dois suportados e informar discretamente o ignorado.
3. Soltar arquivos na área neutra: usar grupo selecionado, primeiro grupo ou criar **Mídias** quando a lista estiver vazia.
4. Reordenar grupos, reordenar itens no mesmo grupo e mover um item para outro grupo.
5. Reiniciar e confirmar que grupo, associação e ordem foram persistidos.
6. Conferir no Explorer que nenhum arquivo foi copiado, movido, renomeado ou apagado.

## Argumentos e instância única

1. Com o GuiaPlay fechado, executar `GuiaPlay.exe "D:\Midias\Abertura.mp4"`: deve abrir e carregar sem reproduzir automaticamente.
2. Repetir com MP3; deve carregar como áudio sem criar saída de vídeo.
3. Executar com arquivo inexistente e formato não suportado; a janela deve permanecer utilizável e mostrar aviso discreto.
4. Com o GuiaPlay aberto e playlist/configurações visíveis, executar novamente com outra mídia.
5. Confirmar que a janela existente vem à frente, recebe a mídia e que a segunda execução termina rapidamente.
6. Confirmar no Gerenciador de Tarefas uma única instância persistente e ausência de perda na playlist/configurações.
7. Confirmar que nenhuma associação de arquivo, entrada de Registro, player padrão ou instalador foi criado.

## Interface e configurações

1. Abrir o programa e conferir o layout: nome da mídia no topo, prévia à esquerda, playlist à direita e faixa operacional embaixo.
2. Redimensionar até 900 × 600 DIPs e testar também em 1366 × 768. Conferir acesso à playlist, progresso, botões, telas, volume e mudo.
3. Abrir a engrenagem. Alterar nomes, operador, aparência e saída de áudio; clicar **Cancelar** e confirmar que nada mudou.
4. Repetir e clicar **Salvar**. Fechar/reabrir o programa e confirmar restauração segura de todas as preferências.
5. Testar Claro, Escuro, Sistema e alto contraste. Confirmar legibilidade, foco, tooltips e estados desabilitados.
6. Clicar **Identificar telas** repetidamente e com Esc; os identificadores devem reiniciar/fechar sem afetar reprodução.

## Playlist virtual

1. Criar os grupos Entrada, Louvor e Encerramento; renomear um deles e reiniciar o aplicativo para conferir persistência e ordem.
2. Selecionar um grupo e adicionar vários vídeos/áudios. Confirmar que só os nomes aparecem na interface e que os arquivos permanecem no local original.
3. Remover um item e confirmar no Explorer que o arquivo original continua intacto.
4. Excluir um grupo e confirmar que nenhum arquivo original foi removido.
5. Adicionar um arquivo de teste, fechar o programa, mover o original e reabrir. A referência deve permanecer riscada/marcada como **arquivo não encontrado**.
6. Restaurar o arquivo ao caminho original e reabrir; o item deve voltar a ficar disponível.

## Vídeo

1. Sem saída pública marcada, abrir um vídeo: **Reproduzir** deve permanecer desabilitado, sem popup e sem seleção automática.
2. Marcar uma saída: **Reproduzir** deve habilitar. Clicar e confirmar início imediato, sem diálogo de confirmação.
3. Parar, marcar duas saídas e reproduzir. Confirmar mesma fonte/relógio, proporção correta e áudio único.
4. Dar duplo clique em vídeo da playlist sem saída selecionada: deve apenas carregar. Repetir com saída marcada: deve carregar e iniciar imediatamente.
5. Testar pausa/continuação, seek, parar, repetir e fim natural. As janelas públicas devem fechar ao parar/finalizar.
6. Desconectar uma saída durante o vídeo e depois todas. Confirmar remoção segura e pausa quando não restar saída pública.
7. Desconectar o operador: painel deve permanecer acessível em tela provisória, vídeo pausado e nova configuração exigida.

## Áudio

1. Desmarcar todas as saídas, abrir MP3/OGG/FLAC e confirmar **Reproduzir** habilitado.
2. Reproduzir e confirmar que nenhuma janela pública é aberta, nenhuma tela fica preta e o conteúdo atual das telas externas não muda.
3. Marcar uma ou duas telas e repetir: o áudio deve continuar somente como áudio e ignorar completamente as seleções de vídeo.
4. Confirmar nome, posição/duração, play/pause/stop, seek, volume e mudo. A prévia pode permanecer preta.
5. Testar **Padrão do Windows** e uma saída USB/HDMI explícita. Confirmar fisicamente onde o som é ouvido.
6. Com saída explícita, testar inicialização indisponível e desconexão durante áudio/vídeo: deve haver bloqueio/pausa sem fallback ou retomada automática.

## Compatibilidade e desempenho

1. Revalidar identidade `QueryDisplayConfig`, nomes, troca de porta, monitor ausente/ambíguo e pré-seleção persistente.
2. Confirmar que uma tela reconectada volta desmarcada durante a sessão.
3. Reproduzir por 30 minutos e registrar CPU, GPU e memória após 1, 10 e 30 minutos.
4. Conferir o log em `%LocalAppData%\GuiaSys\GuiaPlay`, principalmente quadros recebidos/apresentados/coalescidos e média de cópia.
5. Não declarar 4K, codecs específicos, duas saídas físicas ou estabilidade prolongada validados sem executar esses cenários no hardware real.

## M05 — atualizações e instalação física

1. Em Claro e Escuro, abrir **Configurações > Atualizações** em 1366 × 768 e DPI 100%, 125% e 150%; confirmar ausência de cortes e versão `0.5.0-prototipo`/data `25/09/2026`.
2. Clicar **Procurar atualizações** online e confirmar resultado inline. Repetir offline e confirmar erro amigável sem travar mídia, playlist ou telas.
3. Com a versão atual igual à release, confirmar seta de download totalmente oculta e sem espaço reservado.
4. Em teste controlado com uma versão superior, confirmar seta à esquerda da engrenagem e que o clique abre diretamente a guia **Atualizações**, sem iniciar instalação.
5. Instalar `GuiaPlay-Setup-0.5.0-prototipo.exe` por usuário; conferir Menu Iniciar, atalho opcional, desinstalação registrada e execução sem .NET previamente instalado.
6. Confirmar `install.json` somente na instalação do Setup. Executar em `bin/Debug`/`bin/Release` e verificar que instalação automática é recusada sem alterar o projeto.
7. Reinstalar/atualizar em cenário controlado e conferir preservação de `settings.json`, `playlist.json`, nomes de telas, áudio, volume, mudo, aparência e preferências de update.
8. Com áudio e vídeo em reprodução/pausa, habilitar instalação automática e confirmar que nada é fechado/interrompido; o update deve permanecer pendente até o término/parada.
9. Conferir `%LocalAppData%\GuiaSys\GuiaPlay\GuiaPlay.log` e `updater.log` sem tokens ou dados sensíveis.
10. Desinstalar e confirmar remoção dos binários/atalhos, preservando deliberadamente os dados locais do usuário.

## M06 — branding, instalador e atualização real 0.5 → 0.6

1. Abrir o GuiaPlay em 1366 × 768 e 1920 × 1080, com DPI 100%, 125% e 150%; conferir wordmark discreto à esquerda, nome da mídia legível e botões de update/configuração alinhados.
2. Conferir o ícone oficial no arquivo `GuiaPlay.exe`, janela, Alt+Tab e barra de tarefas.
3. Abrir Configurações nos temas Claro, Escuro, Sistema e alto contraste; conferir wordmark, guia **Sobre**, versão `0.6.0-prototipo`, data `26/09/2026` e ausência de cortes.
4. Confirmar que a página do projeto só abre após clique explícito em **Abrir página do projeto**.
5. Executar `GuiaPlay-Setup-0.6.0-prototipo.exe`; conferir ícone do arquivo, imagem lateral da tela inicial, imagem pequena nos cabeçalhos e textos em português do Brasil.
6. Instalar em ambiente controlado com atalho da Área de Trabalho marcado; conferir ícones do Menu Iniciar e da Área de Trabalho, inicialização e instância única.
7. Desinstalar o ambiente controlado; conferir nome/publicador/ícone e confirmar que `%LocalAppData%\GuiaSys\GuiaPlay` não foi removido.
8. Na instalação real `0.5.0-prototipo`, procurar atualização e confirmar `0.6.0-prototipo`, seta de download à esquerda da engrenagem e clique abrindo **Configurações > Atualizações** sem instalar imediatamente.
9. Iniciar download/instalação e confirmar fechamento, aplicação pelo updater, reabertura em `0.6.0-prototipo` e preservação de `settings.json`, `playlist.json`, nomes de telas, áudio, volume, mudo e aparência.
10. Repetir com áudio ou vídeo ativo e confirmar que o update não interrompe a mídia nem é aplicado automaticamente.

## M07 — bug físico de update automático 0.6 → 0.7

1. Instalar `GuiaPlay-Setup-0.6.0-prototipo.exe` e confirmar que **Verificar atualizações automaticamente** está habilitado.
2. Não abrir Configurações.
3. Iniciar o GuiaPlay normalmente e aguardar a consulta em background, sem popup e sem bloqueio da janela.
4. Confirmar que a seta de atualização para `0.7.0-prototipo` aparece automaticamente ao lado da engrenagem.
5. Fechar e abrir novamente antes de 12 horas.
6. Confirmar que a seta reaparece imediatamente pelo estado persistido, sem busca manual.
7. Abrir **Configurações > Atualizações** somente agora e confirmar o mesmo resultado.
8. Clicar **Procurar atualizações** e confirmar que a busca manual consulta novamente mesmo dentro das 12 horas.
9. Após instalar 0.7, reiniciar e confirmar `Current: 0.7.0-prototipo`, `Available: 0.7.0-prototipo`, `Status: UpToDate`.

## M07 — download e controles

1. Iniciar o download de uma atualização e confirmar bytes recebidos, total e percentual reais; a barra não deve saltar por animação artificial.
2. Confirmar os estágios **Baixando atualização**, **Verificando integridade**, **Preparando arquivos** e **Iniciando atualizador**. Após o download, a barra deve ficar indeterminada.
3. Cancelar durante o download e confirmar remoção segura do pacote parcial. Confirmar que cancelamento deixa de ser oferecido nas etapas posteriores.
4. Com áudio e vídeo, girar a roda sobre o volume: cada evento aumenta/diminui 5 pontos, respeita 0..100 e preserva o estado de mudo.
5. Girar a roda sobre a timeline: avançar/retroceder aproximadamente 5 segundos, sem ultrapassar início/fim e com labels imediatos.
6. Clicar em 0%, 50% e 100% da timeline e confirmar seek correspondente; arrastar o thumb deve continuar funcionando sem seek duplicado.
7. Repetir volume e timeline com DPI 100%, 125% e 150% e confirmar que a página/controles pais não recebem scroll indevido.

## M07 — instalador

1. Executar `GuiaPlay-Setup-0.7.0-prototipo.exe` e chegar à última página.
2. Confirmar que **Executar GuiaPlay** existe e começa desmarcado; finalizar sem marcar e verificar que o aplicativo não abre.
3. Repetir, marcar **Executar GuiaPlay** e confirmar abertura após **Finalizar**.
4. Confirmar que **Criar atalho na Área de Trabalho** continua opcional e desmarcado por padrão.
5. Quando marcado, validar que o atalho aponta para `GuiaPlay.exe`, usa o ícone oficial e é removido na desinstalação.
6. Confirmar preservação de `%LocalAppData%\GuiaSys\GuiaPlay`, incluindo settings, playlist, preferências e histórico local existente.

## M08 — integração Windows e Explorer

1. Instalar `GuiaPlay-Setup-0.8.0-prototipo.exe` deixando **Integrar GuiaPlay ao menu Abrir com do Windows** e **Adicionar Abrir com GuiaPlay ao menu de contexto** desmarcadas.
2. Confirmar que o GuiaPlay não assume formatos, não altera players padrão e não aparece como integração habilitada em Configurações.
3. Reinstalar habilitando as duas opções, ou ativá-las em **Configurações > Integração com Windows**.
4. Clicar com o botão direito em um MP4 e localizar **Abrir com GuiaPlay**; no Windows 11, conferir também **Mostrar mais opções**.
5. Abrir o MP4 e confirmar que o GuiaPlay inicia, carrega o arquivo e não reproduz automaticamente.
6. Com o GuiaPlay aberto, usar **Abrir com GuiaPlay** em outro arquivo e confirmar que a mesma instância vem à frente e recebe a nova mídia.
7. Repetir com MP3 e MKV.
8. Testar `C:\Vídeos do Culto\Abertura.mp4` e `D:\Mídia João\Vídeo 01 (Final).mkv`, incluindo acentos, espaços, parênteses e Unicode válido.
9. Testar arquivo em unidade USB; remover a unidade antes de abrir e confirmar aviso inline sem travamento.
10. Testar um caminho UNC acessível, como `\\SERVIDOR\Midias\video.mp4`, confirmando uso do arquivo original sem cópia.
11. Tornar a rede indisponível e confirmar tratamento como arquivo ausente.
12. Usar o botão **Abrir configurações de aplicativos padrão** e confirmar que somente o Windows oferece a escolha; o GuiaPlay não seleciona a si próprio.
13. Desabilitar as duas opções pela interface e conferir a remoção no Explorer.
14. Habilitar novamente, desinstalar o GuiaPlay e confirmar remoção de `GuiaPlay.Video`, `GuiaPlay.Audio`, `RegisteredApplications`, `Capabilities`, `OpenWithProgids` do GuiaPlay e verbos `GuiaPlay.Open`.
15. Confirmar que associações e menus de outros players permanecem intactos após a remoção.
16. Confirmar que `%LocalAppData%\GuiaSys\GuiaPlay` continua preservado e que não existe serviço, processo residente, watcher ou shell extension do GuiaPlay.

## M08 — atualização real 0.7 → 0.8

1. Em uma instalação 0.7 com ou sem integrações existentes, instalar a atualização 0.8 pelo updater.
2. Confirmar que as entradas existentes do Explorer não são apagadas nem duplicadas.
3. Confirmar `Current: 0.7.0-prototipo`, `Available: 0.8.0-prototipo`, `Status: UpdateAvailable` antes do update.
4. Após atualizar, confirmar `Current: 0.8.0-prototipo`, `Available: 0.8.0-prototipo`, `Status: UpToDate`.

## M09 — robustez e validação prolongada prioritária

1. Reproduzir vídeo 1080p por 1 hora em uma saída e registrar diagnóstico/CSV no início e no fim.
2. Repetir vídeo 1080p por 1 hora em duas saídas, registrando RAM inicial/final e CPU aproximada.
3. Observar GPU externamente no Gerenciador de Tarefas; não comparar com uma métrica interna inexistente.
4. Validar MP4/H.264, MKV e WebM; preencher também AVI, MOV, MPEG e TS/M2TS na matriz de performance.
5. Validar MP3 e FLAC; preencher também WAV, OGG, AAC/M4A, WMA e OPUS.
6. Fazer pelo menos 30 trocas de mídia; quando viável, completar 100 com arquivos curtos e conferir geração/log.
7. Fazer seeks rápidos no início, meio e fim enquanto reproduzindo e pausado; testar também roda e clique direto.
8. Testar play/pause/play/stop, replay após Stop e replay após fim natural; nenhuma janela preta deve permanecer.
9. Desconectar/reconectar saída HDMI durante vídeo; as saídas restantes continuam e zero saídas causa pausa segura.
10. Desconectar dispositivo de áudio explícito; confirmar pausa, ausência de fallback e nenhuma janela de vídeo para áudio.
11. Remover USB depois de adicionar a mídia; a referência permanece e a tentativa falha inline sem loop.
12. Indisponibilizar `\\servidor\midia\arquivo.mp4`; confirmar timeout controlado, UI responsiva e nenhuma cópia local.
13. Tentar arquivo vazio, truncado e extensão de mídia com conteúdo inválido; continuar usando o programa depois do erro.
14. Abrir/reordenar/salvar playlist com muitos itens e repetir com 100, 500 e 1.000 referências.
15. Detectar e baixar update durante playback; confirmar reprodução responsiva e aplicação bloqueada até estado seguro.
16. Fechar o app durante vídeo, áudio, pausa e download cancelável; confirmar encerramento e limpeza sem staging parcial.
17. Abrir **Configurações > Diagnóstico**, copiar o texto e conferir ausência de caminho completo, token ou dados privados.
18. Validar a nova guia em Claro, Escuro, Sistema, alto contraste, 1366 × 768 e DPI 100%, 125% e 150%.

## M09 — sessões de 30 minutos, 1 hora e 2 horas

1. Executar `scripts\soak-test.ps1 -Launch -DurationMinutes 30`, iniciar mídia conhecida e guardar o CSV local.
2. Repetir por 60 minutos com 1080p/uma saída e por 60 minutos com 1080p/duas saídas.
3. Executar sessão sequencial de 120 minutos alternando mídia, pausa, stop e seek.
4. Em cada sessão, anotar memória inicial/final, CPU média/pico, frames, erros e responsividade.
5. Preencher `docs/VALIDACAO-PERFORMANCE.md` somente com valores realmente observados.

## M09 — atualização real 0.8 → 0.9

1. Na instalação 0.8, confirmar `Current: 0.8.0-prototipo`, `Available: 0.9.0-prototipo`, `Status: UpdateAvailable`.
2. Baixar, validar SHA-256, aplicar e reiniciar preservando settings, playlist e integração Explorer existente.
3. Após atualizar, confirmar `Current: 0.9.0-prototipo`, `Available: 0.9.0-prototipo`, `Status: UpToDate`.

## M10 — atualização automática definitiva 0.9 → 0.10

1. Instalar `GuiaPlay-Setup-0.9.0-prototipo.exe` e confirmar previamente que **Verificar atualizações automaticamente** está habilitado.
2. Fechar todas as instâncias e iniciar o GuiaPlay normalmente, sem abrir Configurações e sem clicar em busca manual.
3. Aguardar alguns segundos depois de a janela aparecer; confirmar que a interface permanece responsiva.
4. Confirmar que a seta de atualização para `0.10.0-prototipo` aparece automaticamente ao lado da engrenagem, mesmo se a versão 0.9 tiver um cache recente `UpToDate`.
5. Clicar apenas na seta e confirmar abertura direta de **Configurações > Atualizações**, com versão disponível 0.10.
6. Repetir uma abertura sem rede: o programa deve iniciar sem travar; se uma atualização já tinha sido confirmada, seu indicador confiável deve permanecer visível.
7. Desabilitar a verificação automática, reiniciar e confirmar pelo log que nenhuma consulta de startup ocorreu.
8. Reabilitar, baixar e instalar; confirmar SHA-256, fechamento controlado, atualização e reabertura.
9. Confirmar antes do update `Current: 0.9.0-prototipo`, `Available: 0.10.0-prototipo`, `Status: UpdateAvailable`.
10. Após atualizar, confirmar `Current: 0.10.0-prototipo`, `Available: 0.10.0-prototipo`, `Status: UpToDate`.

## M10 — Interface 2.0 e aparência

1. Em 1366 × 768, conferir os cartões de cabeçalho/mídia, prévia, playlist e transporte sem cortes ou sobreposição.
2. Redimensionar até o mínimo e testar DPI 100%, 125% e 150%; conferir timeline, volume, telas, Abrir, Reproduzir, Pausar e Parar.
3. Abrir Configurações e navegar por teclado entre **Telas**, **Aparência**, **Áudio**, **Atualizações**, **Windows**, **Diagnóstico** e **Sobre**.
4. Testar Sistema, Claro e Escuro com Azul GuiaPlay, Ciano, Roxo, Verde, Laranja e Rosa. Conferir prévia, contraste, foco, estados desabilitados e persistência após reiniciar.
5. Com Sistema selecionado, mudar o modo de aplicativos do Windows e confirmar a atualização do tema.
6. Ativar alto contraste e conferir que cores do sistema prevalecem sobre o destaque salvo e que texto/controles continuam legíveis.
7. Confirmar que Cancelar não persiste mudanças feitas na prévia; Salvar deve restaurá-las na próxima execução.

## M10 — equalizador nativo

1. Confirmar que o equalizador começa desativado e que áudio/vídeo reproduzem como antes.
2. Ativar o equalizador durante um MP3 e testar Flat, Rock, Classical e outros presets oferecidos pelo LibVLC instalado; confirmar mudança audível sem reiniciar a mídia.
3. Alterar preamp e bandas individualmente; confirmar que o seletor passa a **Personalizado**.
4. Testar os limites -20 e +20 dB e conferir ausência de valores inválidos, distorção de UI ou travamento.
5. Salvar, reiniciar e confirmar restauração de ativação, preset/personalizado, preamp e bandas.
6. Repetir com vídeo, troca de mídia e troca de dispositivo de áudio; a equalização deve ser reaplicada sem criar outro player.
7. Se o LibVLC reportar indisponibilidade/falha do recurso, confirmar mensagem amigável e reprodução normal sem equalizador.

## M10 — regressão física preservada

1. Abrir MP4/MP3/MKV pelo Explorer, inclusive com o GuiaPlay já aberto, e confirmar mesma instância, janela à frente e carregamento sem autoplay.
2. Conferir que ProgIDs, Capabilities, **Abrir com** e o verbo opcional permanecem e que nenhum player padrão foi forçado.
3. Validar vídeo 1080p em uma e duas saídas, áudio sem saída visual, seek, pausa, stop/fim natural e pelo menos uma sessão prolongada do roteiro M09.
4. Conferir tema/contraste em hardware real e ouvir presets/equalização em dispositivo conhecido; testes automatizados não substituem essa percepção física.
5. Confirmar que o Setup 0.10 continua per-user, sem administrador, com tarefas de Explorer e execução final desmarcadas por padrão.
