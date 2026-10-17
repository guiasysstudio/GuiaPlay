# GP-I08 — YouTube: parecer de viabilidade, prova isolada e decisão técnica

**Data da análise:** 16/10/2026.  
**Branch:** `feat/gp-interface-3-modelo1`.  
**Resultado:** **GO CONDICIONADO** para incorporar **um player oficial visível** do YouTube e comandá-lo por IFrame API; **NO-GO** para prometer o espelho idêntico de quadros do LibVLC ou múltiplas saídas WebView2 sincronizadas como se fossem um único motor.  
**Status de execução:** análise documental e código do spike presentes; **não houve build, navegação WebView2 ou execução do HTML no Windows**, por decisão expressa de executar os testes integrados após GP-I10.

## 1. Fontes oficiais consultadas

1. YouTube IFrame Player API: https://developers.google.com/youtube/iframe_api_reference
2. YouTube Embedded Players and Parameters: https://developers.google.com/youtube/player_parameters
3. YouTube Required Minimum Functionality: https://developers.google.com/youtube/terms/required-minimum-functionality (**atualizada em 15/10/2026**).
4. YouTube Developer Policies: https://developers.google.com/youtube/terms/developer-policies
5. Microsoft WebView2 para WPF: https://learn.microsoft.com/en-us/microsoft-edge/webview2/platforms/wpf
6. Microsoft WebView2 segurança e mensagens: https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/security
7. WebView2 WPF, inicialização e comunicação: https://learn.microsoft.com/en-us/microsoft-edge/webview2/get-started/wpf

Estas políticas e APIs podem mudar. Revisar antes da comercialização e durante a manutenção.

## 2. Capacidades documentadas (GO condicionado)

- O player oficial YouTube via **IFrame Player API** possui `playVideo`, `pauseVideo`, `stopVideo`, `seekTo`, `setVolume`, `mute`, `unMute`, `getCurrentTime`, `getDuration` e eventos `onReady`, `onStateChange`, `onError`, `onAutoplayBlocked`.
- WPF pode hospedar um WebView2 (Edge Chromium), com troca de comandos/telemetria em JSON pelo canal `CoreWebView2.PostWebMessageAsJson` e `WebMessageReceived`. Esse WebView é **outro motor de reprodução**; os controles do GuiaPlay podem estar na mesma posição visual, mas não se deve fingir que a mídia está no LibVLC.
- Um **único** player incorporado *visível* em uma tela pública, com o operador enviando comandos à janela e recebendo estado/tempo, é o caminho candidato mais seguro. A tela pública apresenta o player original com identificação, interface e anúncios.
- A integração não requer login OAuth do usuário para vídeos públicos incorporáveis, nem chave da Data API apenas para os comandos IFrame; **requer identificação apropriada do aplicativo**/Referer e políticas aplicáveis.
- Vender um **API Client** é permitido em termos gerais, desde que cumpridas as políticas, mas cobrar especificamente por assistir vídeo incorporado, manipular publicidade, remover branding ou redistribuir o conteúdo não é permitido. Avaliar comercialização/licença do GuiaPlay e termos de consentimento antes de entregar ao cliente.

## 3. Bloqueios técnicos ou contratuais (NO-GO para paridade 1:1)

| Requisito do GuiaPlay | Veredicto do GP-I08 |
|---|---|
| Play/Pause/Stop/volume/seek **pela barra do GuiaPlay** | API documenta comandos equivalentes; possível integrar condicionalmente. Eventos, duração e seek dependem do vídeo e da disponibilidade da API. |
| Um único áudio/vídeo decodificado do qual se extraem frames para o `WriteableBitmap` existente | **Não suportado pela API oficial**. Não extrair stream, capturar frames do player ou desviar reprodução para VLC. |
| **Prévia ao vivo idêntica** no operador e imagem no projetor, como vídeo local | **Não demonstrado nem garantido** com API oficial. Uma segunda janela com segundo iframe é outro player, com buffers/latência próprios. |
| Dois ou mais projetores exatamente sincronizados | **Não garantido** por players IFrame independentes. Não prometer sincronismo de quadro/áudio entre browsers. |
| Player do YouTube dentro das duas prévias pequenas do Modelo 1 | **Potencialmente incompatível com viewport mínimo**: player precisa de **ao menos 200 × 200 pixels**; recomendado **480 × 270**. O painel de miniaturas não deve hospedar iframe minúsculo. |
| Ocultar publicidade, marca do YouTube ou botões do player em favor de um espelho WPF próprio | **Não permitido** interferir/encobrir o player; a UI operacional pode ficar **fora** da área visível do iframe. |
| Reprodução automática e player escondido | **Não permitido** iniciar oculto; mais da metade da área do player deve estar visível. Não iniciar automaticamente mais de um player do YouTube na mesma tela. |
| Conteúdo bloqueado para incorporação, região, idade ou não listado sem autorização | Respeitar restrições e informar erro. **Jamais contornar** via URL de stream, cookies de terceiros, HTML scraping ou downloader. |

> A regra de viewport é em pixels reais. O GuiaPlay usa WPF em DIPs e deve converter/medir em cada monitor e DPI antes de criar a janela com iframe.

## 4. Identificação do aplicativo e política comercial: bloqueios de aceite

- Desde as políticas de identificação de 2025, a incorporação exige `HTTP Referer`/equivalente identificando o API Client; WebViews de desktop **podem omitir** esse header. Documentação oficial sugere `CoreWebView2HttpRequestHeaders.SetHeader` para .NET/WebView2. O erro IFrame **153** sinaliza ausência da identificação.
- O executável atual contém `app.manifest` com `assemblyIdentity name="GuiaPlay.app"`, mas **não é prova de package identity MSIX/Windows registrada**. Não inventar identidade ou forjar Referer; validar o identificador correto para distribuição Inno Setup e a origem de hospedagem do iframe.
- Se usar página local, preferir WebView2 com **virtual HTTPS host mapeado** e `origin` correspondente, com origem segura e Referer validado, em vez de `file://`, `about:blank` ou `NavigateToString` sem identidade estável.
- Manter controles, anúncios, logos, origem e capacidade de clicar no player; não colocar overlays sobre iframe.
- Expor termos do YouTube e política de privacidade do Google, atualizar os termos/privacidade do GuiaPlay, coletar apenas telemetria local mínima do player e nunca credenciais.
- **Não cobrar taxa adicional nem criar paywall especificamente para assistir ao YouTube embutido**. O modelo de licenciamento do aplicativo como um todo exige revisão jurídica/produto para garantir conformidade, além do direito de exibição pública do conteúdo.
- A reprodução em cultos e eventos públicos pode exigir direitos de execução/exibição dos detentores das obras; permissão técnica de embed **não concede** automaticamente esses direitos.

## 5. Segurança do WebView2 na próxima fase

- Um único WebView2 público/visível. Proteger o monitor do operador; bloquear alteração de destino durante sessão ativa; não abrir novas janelas indiscriminadamente.
- Criar/usar WebView2 **na thread de UI**, com inicialização assíncrona; não chamar `.Wait()` nem `.Result` no Dispatcher.
- Fazer allowlist de **origem exata** nas navegações e validar **`WebMessageReceived.Source`** no host nativo.
- Trocar mensagens por **JSON de esquema fechado** (`play`, `pause`, `stop`, `seek`, `volume`, `mute`, `unmute`, telemetria). Nunca executar JavaScript interpolado com URL do usuário.
- Interromper/destruir o player ao fechar janela, perder licença ou monitor, tratar `onAutoplayBlocked`, timeout, `onError` (2, 5, 100, 101, 150, 153) e desconexão de rede.
- Não expor segredo GSL-v1, dados de licença ou caminhos locais ao iframe. Usar pasta/profiling isolados do WebView; atenção à privacidade/cookies.
- LibVLC local permanece inalterado e padrão para vídeos locais. Não permitir dois mecanismos independentes aparentarem ter um único clock.

## 6. Código entregue nesta etapa

- `src/GuiaPlay.Core/YouTubeVideoReference.cs`: normalização estrita de links oficiais, video ID de 11 caracteres e construção segura de URL de embed com `enablejsapi`, `origin`; não abre rede.
- `src/GuiaPlay.Core/YouTubeEmbedGate.cs`: política pura de viewport mínimo, identidade, player visível e autoplay; explicita ausência de suporte ao espelho de quadros do LibVLC e garantia de sincronismo.
- `spikes/gp-i08-youtube/iframe-player.html`: **estudo isolado**, com `YT.Player` oficial *visível*, botão e marca oficiais intactos, bridge JSON, estado, duração, seek, volume, erros e autoplay bloqueado. **Não está incluído no build nem conectado à UI.**
- `tests/GuiaPlay.Core.Tests/YouTubeVideoReferenceTests.cs`: casos de URL válida/inválida, domínios falsos, IDs ambíguos e origem segura, **escritos, não executados**.
- `tests/GuiaPlay.Core.Tests/YouTubeEmbedGateTests.cs`: casos de geometria, Referer, visibilidade e bloqueio de autoplay, **escritos, não executados**.
- `docs/PLANO-INTERFACE-3-MODELO1.md`: resultado do gate.

Não foram adicionadas dependências NuGet, menus ou botões de YouTube à aplicação, porque seria enganoso mostrá-los antes de existir um backend comprovado.

## 7. Decisão de produto proposta para GP-I09

**GO condicionado: implementar o YouTube como um tipo de mídia oficial separado, com player WebView2 em janela pública visível, um único iframe e controles auxiliares do operador integrados à mesma região de transporte (com estados reais), SEM prometer prévia de vídeo em miniatura nem múltiplos projetores sincronizados.**

A Prévia do Operador exibe *informações reais* do player (título quando permitido, estado, duração/posição/erros), claramente identificada como **YouTube / sem espelho ao vivo**, enquanto a janela pública contém o vídeo original. O espaço da interface se mantém, mas **o comportamento de vídeo local (prévia idêntica) não pode ser afirmado para YouTube**.

**Gate de aceitação da implementação GP-I09:** se a exigência comercial for especificamente *dois vídeos simultâneos sincronizados e imagem ao vivo idêntica em ambos os previews*, **não liberar YouTube como recurso equivalente**. Manter esse subrequisito como impedimento de produto até conseguir autorização/solução tecnicamente conforme; **nunca substituir por stream ripping nem captura de tela oculta**. Somente liberar o fluxo reduzido com identificação honesta e aceite expresso desse comportamento.

### Persistência/compatibilidade a planejar na GP-I09

`PlaylistDocument` atual tem schema v1, `PlaylistItem.OriginalPath` e `PlaylistDocumentCodec.Normalize` usam `Path.GetFullPath`, e `MediaTypeDetector` detecta arquivo local. **Não salvar URL YouTube no campo OriginalPath da v1**, nem passar URI à sonda de arquivos/LibVLC. Implementar representação discriminada e migração/codec de schema v2, preservando sem perda todos os presets/arquivos v1 e separando player backend. Revisar também `PlaylistPresetStore`, `FolderMediaPlaylistMerge`, UI de grupos/tabela, ativação, licença, tempos, atualização e compatibilidade de downgrade. Fazer isso na GP-I09, sem modificar dados existentes nesta etapa.

### Próximos testes no GP-I10

- Diagnóstico de Referer/erro 153 em WebView2 x86/x64; políticas de cookies, origem e consentimento.
- Um vídeo incorporável e um com embed bloqueado; vídeo privado/removido, rede intermitente, 101/150/100/5, bloqueio de autoplay.
- Operador+projetor; janela visível 480×270+ e DPI; fechamento da tela pública durante reprodução; ausência de áudio duplo.
- Invariantes: reprodução local LibVLC, sem segundo player oculto, sem captura/extração/redistribuição de YouTube.
- Aceite explícito de diferença entre status no operador e preview ao vivo local antes de anunciar integração como concluída.

**Sem build, teste físico, instalador, tag, GitHub Release ou modificação da branch `main`.**
