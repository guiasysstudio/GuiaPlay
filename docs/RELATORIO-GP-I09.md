# GP-I09 — Integração YouTube via WebView2 / IFrame oficial

Data de implementação: 16/10/2026  
Branch de desenvolvimento: `feat/gp-interface-3-modelo1`  
Estado: **implementado no código como integração de capacidade limitada; NÃO COMPILADO/TESTADO NO WINDOWS e NÃO LIBERADO COMERCIALMENTE.** Regras acordadas: executar a validação integrada somente no GP-I10 e nunca publicar instalador, tag ou Release antecipadamente.

## Funcionalidade programada

1. **Adicionar ▾ > Adicionar link do YouTube…** no painel de playlists, com janela de entrada de URL (até 2.048 caracteres), nome opcional e validação estrita de vídeo. Não aceita qualquer host semelhante a youtube.com, URL HTTP nem outros formatos que não identifiquem vídeo.
2. O vídeo é incorporado à **playlist real** como `MediaKind.YouTube`, com ID GUID, grupo, ordem e URL HTTPS canônica, sem arquivo fantasma no disco. A entrada pode ser salva em um preset, carregada, removida ou reordenada como mídia da playlist. Link duplicado dentro do mesmo grupo é rejeitado.
3. Esquema de playlist passa a **v2**; leitura de documentos v1 permanece aceita. `PlaylistDocumentCodec` não chama `Path.GetFullPath` para itens YouTube, nem passa URLs à sondagem do filesystem. `PlaylistPresetStore` aceita presets v1/v2, gravando v2. Antes da primeira sobrescrita de documento/preset v1, o app guarda um arquivo `.pre-youtube-v1.bak`.
4. Colunas e árvore exibem tipo **YouTube**, identificação na tabela, caminho/link original e ausência honesta de metadados locais (`—`, `Web`). Não inventam miniatura, resolução nem data. As sondagens de mídia local ignoram links Web.
5. Pacote NuGet `Microsoft.Web.WebView2` estável **1.0.4258.31**; HTML oficial do player (`Assets/YouTube/player.html`) incluído no output para Windows.
6. `YouTubeOutputWindow`: janela **pública única** com WebView2 e player oficial IFrame visível, na tela selecionada. Exige **exatamente um monitor público** separado do monitor do operador e viewport >=200x200 pixels reais. URLs desconhecidas não carregam como documentos superiores; mensagens WebView2 são aceitas somente da origem virtual HTTPS do aplicativo, com JSON de estados e comandos controlados. Não há extração de stream, vídeo baixado ou captura do player.
7. `HTTP Referer` explicitado a partir da origem HTTPS identificadora do aplicativo `https://guiaplay-assets.guiasys.online/` para a incorporação YouTube. **Precisa ser conferido em WebView2 real** (especialmente erro IFrame 153 e identidade comercial) antes da distribuição.
8. O player envia `ready`, `state`, `position` (até 2x/s), `error` e `autoplayBlocked`; host distingue pronto, reproduzindo, pausado, concluído, erro, interação necessária e buffering. Há timeout de inicialização e mensagens específicas para 5/100/101/150/153.
9. Barra do operador existente passa a encaminhar **Reproduzir, Pausar, Parar, posição da timeline, ±5 s, volume e mudo** ao IFrame quando esse backend está ativo. Barra/tempo usam duração recebida do player, habilitando seek somente se disponível. F1 e demais atalhos que chamam handlers continuam relacionados às mesmas funções.
10. Painel **Programa** mostra o estado real e a informação honesta de que **não existe preview de quadros YouTube no operador**. O player oficial permanece visível exclusivamente no monitor público; não se apresenta espelho fictício do LibVLC.
11. **Mídia anterior/próxima** respeita URLs reais na playlist, mas exige confirmação se houver reprodução ativa e não começa automaticamente. Para YouTube, reprodução é **sempre manual**; modos de repetição, sequência e aleatório ficam indisponíveis, em vez de simular paridade não suportada. A sequência de vídeos locais para quando alcança uma entrada YouTube.
12. Ao alternar para vídeo local, encerrar o app, perder a licença, desconectar tela pública/operador ou pressionar Parar, o player WebView2 público é fechado; LibVLC mantém sua base local. Atualização automática e mudanças de seleção/roteamento de monitores são bloqueadas durante o player ativo.
13. Áudio WebView2 utiliza o caminho padrão do Windows (mixer do sistema), **não a seleção explícita de dispositivo do LibVLC**. O painel indica essa diferença, sem prometer roteamento inexistente. Botões de mudo/volume do GuiaPlay encaminham comandos ao player.
14. Links externos do próprio YouTube solicitados por interação do usuário podem abrir no navegador padrão; pop-ups WebView2 arbitrários não são criados.

## Arquivos criados

- `src/GuiaPlay.App/Windows/YouTubeOutputWindow.cs`
- `src/GuiaPlay.App/Windows/YouTubeLinkPromptWindow.cs`
- `src/GuiaPlay.App/Assets/YouTube/player.html` (adaptado do spike GP-I08)
- `tests/GuiaPlay.Core.Tests/YouTubePlaylistIntegrationTests.cs` (testes escritos; não executados)
- `docs/RELATORIO-GP-I09.md`

## Arquivos alterados

- `src/GuiaPlay.Core/MediaTypes.cs`
- `src/GuiaPlay.Core/Playlist.cs`
- `src/GuiaPlay.Core/PlaylistDocumentCodec.cs`
- `src/GuiaPlay.Core/PlaylistPresets.cs`
- `src/GuiaPlay.App/GuiaPlay.App.csproj`
- `src/GuiaPlay.App/MainWindow.xaml`
- `src/GuiaPlay.App/MainWindow.xaml.cs`
- `src/GuiaPlay.App/Models/PlaylistTreeNodes.cs`
- `src/GuiaPlay.App/Models/MediaGridRow.cs`
- `src/GuiaPlay.App/Services/MediaGridMetadataService.cs`
- `docs/PLANO-INTERFACE-3-MODELO1.md`

## Gate obrigatório em GP-I10

- Compilar Debug/Release x86/x64 e executar todos os testes, incluindo o codec de esquema v1/v2, arquivos locais, presets, backup e URL.
- Validar WebView2 Runtime instalado, seu carregamento por host HTTPS virtual, identidade/Referer aceitos sem erro 153, cross-origin, eventos do IFrame e reprodução visível. Se falhar, **não classificar YouTube como pronto** e corrigir em GP-I11+.
- Usar pelo menos vídeo incorporável, vídeo privado/removido, incorporação bloqueada, rede offline, vídeo ao vivo/duração 0, região restrita e autoplay bloqueado.
- Testar operador + um projetor, + dois projetores (mostrar limitação), perda de operador/saída, DPI 100/125/150, 1366x768 e 1920x1080.
- Confirmar inexistência de áudio duplicado, redução de latência, atraso no volume/seek; conferir UI do YouTube e links de atribuição preservados.
- Confirmar que alternar YouTube ↔ LibVLC, salvar/reabrir playlists/presets, buscar, reordenar, excluir, voltar versão e recuperar backups preserva dados e não inicia mídia automaticamente.
- Revisar documentação de licenciamento/produto, direitos de exibição pública e termos YouTube; **a possibilidade técnica de IFrame não autoriza automaticamente comercializar exibição de conteúdo.**

## Limitações definitivas desta modalidade

**Não implementado:** preview de vídeo ao vivo do YouTube no operador; sincronismo de dois projetores YouTube; captura, download ou extração de stream; roteamento de áudio WebView2 para dispositivo LibVLC; reprodução automática sem interação; paridade completa da transição sequência/repetição/aleatório com mídia local.

**Decisão de produto:** o usuário avançou para GP-I09 após receber o parecer GP-I08; isto autorizou implementar o **modo limitado numa branch experimental**, **não substitui aprovação de liberação comercial**. Sem teste real e consentimento explícito sobre ausência de prévia ao vivo, o catálogo/marketing não pode prometer equivalência com vídeos locais.

## Revisão estática

- XAML, nomes únicos, event handlers, recursos de tema, dependência WebView2, página do player, fluxo de reconhecimento de URLs, compatibilidade inicial do codec e esquema de testes foram revisados via código do repositório.
- **Nenhum `dotnet build`, `dotnet test`, execução do WebView2, teste físico, instalador, release ou push na main nesta etapa.**
