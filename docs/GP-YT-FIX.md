# GP-YT-03 — YouTube: cadastro separado da reprodução por tela pública

Data: 17/10/2026. Branch: `feat/gp-interface-3-modelo1`.
Substitui o fluxo provisório GP-YT-02 de janela flutuante.

## Fluxo operacional

1. Adicionar link do YouTube à pasta de uma playlist salva. O botão
   **Adicionar link** apenas persiste e seleciona a linha; NÃO abre WebView2
   nem inicia vídeo.
2. Escolher exatamente uma tela pública na lista **Saída de vídeo**.
   O monitor do operador não pode ser o destino. Se nenhuma tela ou várias
   estiverem marcadas, mostrar mensagem, sem criar janela flutuante.
3. Clicar **Reproduzir** na barra central, no ícone da mídia ou dar duplo clique
   na mídia cadastrada. Esta ação explícita é que abre o único YouTube
   IFrame Player oficial, **em tela cheia na saída pública escolhida**.
4. O vídeo solicita `autoplay=1` à API oficial e um `playVideo()` após
   `onReady`. Isso significa início automático APÓS o Play do GuiaPlay,
   nunca ao colar o link. Bloqueio de autoplay com áudio pode ocorrer;
   o evento `onAutoplayBlocked` informa ao operador que precisa clicar
   no Play visível do player oficial da tela pública.
5. Play, pause, seek, volume e mute permanecem na barra do GuiaPlay,
   com estado confirmado pela IFrame API. Stop fecha o player público.

## Segurança/limitações

- Um único player oficial visível; sem duplicação oculta, captura de
  quadros, remoção de marcas ou anúncios, re-streaming ou bypass de embed.
- A prévia do operador só mostra informações/estado. Não oferece o mesmo
  espelhamento do LibVLC ou sincronismo entre projetores.
- O YouTube pode negar embed (erros 100, 101, 150, 153, etc.), exigir
  ação direta do usuário, impedir autoplay com áudio ou depender de rede.
- WebView2 Runtime precisa estar disponível no Windows. A tentativa de
  instalação automática aplica-se aos instaladores, não ao `dotnet run`.

## Aceite manual obrigatório

- Adicionar link e confirmar que NENHUMA janela abre nesse momento.
- Selecionar a Tela 2, pressionar Play; verificar que o vídeo abre SOMENTE
  na Tela 2, em tela cheia, sem popup flutuante na tela do operador.
- Pausar, retomar, procurar posição, ajustar volume e fechar a saída.
- Com 0 ou 2 telas selecionadas, confirmar erro compreensível e
  ausência de popup inesperado.
- Repetir o link já salvo sem iniciar playback.
- Confirmar que mídias locais LibVLC, configurações e licenciamento
  permanecem sem regressões.
- Validar o comportamento real do autoplay: sucesso ou indicação visível
  de bloqueio; não afirmar que autoplay com áudio é garantido.

Os testes automatizados não substituem a validação real de vídeo online
no computador do operador.
