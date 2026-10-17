# GP-YT-FIX — player oficial do YouTube dentro do GuiaPlay
Data: 17/10/2026. Branch: `feat/gp-interface-3-modelo1`.

## Motivo da falha percebida
Na implementação anterior o link era salvo, porém o player só podia ser
carregado com **exatamente uma tela pública** selecionada e um monitor de
operador configurado. Isso impedia a reprodução em computadores com só
um monitor ou sem saída pública escolhida.

## Comportamento corrigido
- Colar link → "Adicionar e abrir" salva na playlist e **abre o player**.
- Sem saída pública selecionada: abre uma janela do GuiaPlay no operador,
  redimensionável, com um único YouTube IFrame Player oficial visível.
- Com uma saída pública: abre um único YouTube IFrame na tela pública.
- Com duas ou mais saídas: não oferece sincronismo inexistente; pede para
  selecionar no máximo uma. O operador recebe status e tempo, não um
  segundo vídeo oculto ou outro iframe fora de sincronia.
- Play/pause/seek/volume/mute da barra existente continuam usando a
  IFrame Player API e os eventos oficiais. O início do vídeo é manual.
- Erros da página WebView2, de processo, timeout e códigos do YouTube
  são expostos e registrados no log. O embed mantém identificação,
  controles, marca e anúncios da plataforma.
- Instaladores Inno Setup (stable/test): detectam WebView2 Runtime no
  HKCU/HKLM 32/64; se faltar, buscam o bootstrapper Evergreen oficial da
  Microsoft e tentam instalá-lo para o usuário. Se offline, terminam a
  instalação do GuiaPlay e indicam onde obter o runtime manualmente.
  O `dotnet run` de desenvolvimento não executa o instalador.

## Validação manual obrigatória (Windows)
1. Atualizar a branch, fechar o GuiaPlay e executar em Debug.
2. Criar uma playlist e pasta, não selecionar saída pública.
3. Adicionar link `https://www.youtube.com/watch?v=M7lc1UVf-VE`.
4. Conferir nova janela do GuiaPlay e reprodução pelo botão nativo
   ou pelo botão Reproduzir; testar pausar, procurar e mudar volume.
5. Fechar a janela e conferir que o aplicativo não fica com áudio rodando.
6. Selecionar uma tela pública, reabrir o item da playlist e testar
   controle remoto da barra, estado e duração.
7. Testar vídeo privado/sem permissão de embed, sem rede, player indisponível.
8. Testar instaladores isolados x64/x86 em Windows sem WebView2 Runtime
   e com runtime pré-instalado. Verificar logs e comportamento offline.

**Limitações:** não há captura de quadro YouTube/LibVLC, prévia ao vivo
sincronizada ou múltiplos projetores para um único vídeo YouTube.
Vídeos protegidos (idade, região, embed proibido) não podem ser
desbloqueados pelo GuiaPlay. A aprovação final depende da reprodução real
no computador do operador; build e testes de CI não provam vídeo online.
