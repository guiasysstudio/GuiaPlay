# Roteiro manual — M04 fluxo operacional e integração com Windows

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
