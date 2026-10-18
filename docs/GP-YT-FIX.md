# GP-YT06 — Réplicas do player dentro do GuiaPlay (protótipo experimental)

Data: 17/10/2026. Branch `feat/gp-interface-3-modelo1`.

## Requisito do operador

- Três ou mais monitores devem permanecer **estendidos e independentes no Windows**.
- Tela do sonoplasta mantém a interface; saídas públicas ficam selecionáveis.
- Para YouTube: um único player oficial WebView2 na primeira saída selecionada;
  as outras recebem réplicas gráficas ao vivo da **janela inteira** do player.
- Não configurar o Windows em modo Duplicar. O botão antigo 'Espelhar' que
  abria Configurações do Windows foi retirado.
- Não alterar a lógica dos arquivos locais/LibVLC, nem o número de monitores.

## Técnica avaliada

O DWM oferece `DwmRegisterThumbnail` / `DwmUpdateThumbnailProperties`
para estabelecer uma ligação dinâmica entre duas janelas top-level.
`YouTubeMirrorOutputWindow` usa exclusivamente uma janela do GuiaPlay
como fonte e uma janela de destino por outro monitor. Não executa outro
WebView2, não acessa o stream do YouTube, não armazena frames e não muda
a topologia dos monitores. A janela de origem deve permanecer visível.

**Isto é um protótipo, não uma confirmação de viabilidade comercial.**
O navegador pode renderizar conteúdo protegido ou superfícies aceleradas
sem expor os quadros às miniaturas do DWM: telas pretas, quadros estáticos
ou janelas vazias são possibilidades reais. O DWM não fornece teste
automático conclusivo de exibição real no projetor.

**Licenciamento e políticas externas:** as políticas de desenvolvedores do
YouTube restringem a redistribuição e a separação dos componentes de áudio
e vídeo. Antes de disponibilizar a replicação de seu conteúdo em software
comercial é necessária uma avaliação de conformidade/autorização adequada.
Nenhum bloqueio de anúncios, DRM, privacidade ou restrições é contornado.

## Fluxo / gates

1. Adicionar link: só cadastra, sem abrir.
2. Reproduzir em uma tela: player oficial, igual ao atual.
3. Reproduzir em duas ou mais telas: 1 WebView2 + N-1 réplicas DWM, áudio
   apenas no player de origem. Play/Pause/Seek/Stop dirigidos somente ao
   player original. Não existe segundo buffer de vídeo.
4. Fechar um monitor / perda de sessão: encerrar a sessão YouTube inteira.
5. Retornar ao vídeo local: comportamento LibVLC original, inalterado.
6. Autoplay com áudio: uma solicitação após `onReady`; navegador pode
   bloquear. Uma única tentativa automática sem áudio, seguida por
   mensagem clara se bloqueada.

## Validação física obrigatória, antes de habilitar em produção

- Configurar três monitores em **Estender**.
- Selecionar projetores 2 e 3 e reproduzir YouTube com áudio.
- Confirmar que há 1 player na Tela 2 e imagem **viva, não congelada nem preta**
  na Tela 3. Comparar avanço, propagandas, tempo, controles e volume.
- Reproduzir 20 minutos, redimensionar/DPI, alterar foco, fechar,
  desconectar monitor e reconectar, sem destruir as preferências do operador.
- Testar também quatro telas; medir GPU/RAM e apresentar falha legível
  quando a replicação não for suportada.
- Validar legalmente os direitos de retransmissão/redisplay do conteúdo,
  inclusive anúncios, antes de distribuir o recurso comercialmente.

O GitHub Actions prova build e testes existentes, mas não valida DWM/YouTube
sob aceleração real, nem garante autoplay com áudio.
