# Relatório de implementação — M03

Data: 25/09/2026  
Versão: 0.3.0-prototipo

## Resultado

O M03 transforma a janela principal em painel de operação ao vivo: nome da mídia no topo, prévia menor, playlist/cronograma à direita e controles imediatos na faixa inferior. Nomes de telas, operador, aparência e dispositivo de áudio foram reunidos na janela aberta pela engrenagem, com edição isolada até **Salvar** e preservação de **Cancelar**.

O diálogo de confirmação anterior ao início da reprodução foi removido. Abrir mídia apenas carrega; **Reproduzir** inicia imediatamente quando a política central permite. O caminho completo da mídia carregada não é exibido na janela principal.

## Arquitetura da playlist

`PlaylistCatalog` concentra criar, renomear e excluir grupos virtuais, adicionar/remover itens e normalizar ordens. Grupos e itens possuem `Guid` e campo `Order`, deixando o modelo preparado para reordenação posterior sem exigir drag-and-drop agora.

`PlaylistStore` persiste separadamente em `%LocalAppData%\GuiaSys\GuiaPlay\playlist.json`, com schema 1, arquivo temporário, flush e substituição atômica. O arquivo contém apenas:

- ID, nome e ordem do grupo;
- ID, apresentação e ordem do item;
- caminho absoluto original;
- classificação áudio/vídeo.

Nenhum fluxo lê a mídia inteira para cadastrá-la, copia/move arquivos ou serializa bytes. Remover um item ou grupo altera somente o catálogo. A disponibilidade é avaliada pelo caminho original; referências ausentes permanecem no cronograma e aparecem riscadas como **arquivo não encontrado**.

## Regra áudio x vídeo

`PlaybackCoordinator.CanPlay` é a fonte única da regra:

- vídeo válido: exige uma ou mais saídas públicas selecionadas;
- áudio válido: independe das saídas selecionadas;
- sem mídia ou formato desconhecido: não permite reprodução.

O estado do botão acompanha essa política ao carregar mídia, marcar/desmarcar telas, pausar, parar, finalizar ou falhar. Duplo clique na playlist chama o mesmo fluxo: áudio inicia; vídeo inicia apenas com saída selecionada; não há confirmação intermediária nem seleção automática.

`PlaybackRouting` declara se a mídia usa saídas de vídeo. Para áudio, a interface nunca cria `OutputWindow`. O motor adiciona `:no-video` e não instala callbacks de quadros, portanto não abre tela preta, não limpa e não altera telas externas, mesmo que estejam marcadas. Há um único `MediaPlayer`, preservando dispositivo configurado, volume, mudo, posição e baixo consumo.

## Formatos

A seleção/classificação inicial aceita formatos comuns que serão efetivamente decodificados pelo LibVLC:

- vídeo: MP4, MKV, AVI, MOV, WMV, WebM, M4V, MPG/MPEG, TS/M2TS, 3GP e OGV;
- áudio: MP3, WAV, OGG, FLAC, AAC, M4A, WMA, Opus, AIFF e ALAC.

Não foram adicionados codecs, geradores de thumbnail, waveform, polling de mídia nem bibliotecas pesadas. Arquivo com extensão aceita mas conteúdo/codec inválido continua sujeito ao erro normal do LibVLC.

## Persistência e compatibilidade

`settings.json` evoluiu para schema 3 e mantém o leitor tolerante dos schemas anteriores, preservação de propriedades desconhecidas, validação, recuperação de JSON inválido e gravação atômica. A playlist separada evita misturar preferências permanentes com o cronograma e mantém o settings leve.

Foram preservados `QueryDisplayConfig`, identidade persistente, restauração conservadora, monitores desconectados, pré-seleção, dispositivo LibVLC, volume/mudo, aparência e configurações Salvar/Cancelar. Perda de telas não pausa áudio; perda da saída de áudio configurada continua pausando a mídia sem fallback silencioso.

## Testes e validações executadas

- Build Debug: aprovado, 0 avisos e 0 erros.
- Testes Debug: 45/45 aprovados.
- Build Release: aprovado, 0 avisos e 0 erros.
- Testes Release: 45/45 aprovados.
- `dotnet format --verify-no-changes`: aprovado.
- Smoke Release com `settings.json` e `playlist.json` isolados: processo iniciou, criou a janela `GuiaPlay 0.3.0-prototipo` e permaneceu responsivo.
- O conector de inspeção visual retornou inventário vazio e não conseguiu anexar à janela WPF; o smoke de processo não é apresentado como aprovação visual.
- Nenhum settings real foi alterado e os temporários do smoke foram removidos.

Os testes automatizados cobrem política de reprodução sem/uma/múltiplas telas, independência e roteamento somente-áudio, ausência de mídia, formatos, grupos, renomeação, ordem, persistência, referência sem payload, remoção sem apagar original, arquivo ausente, migração M02 e toda a suíte anterior.

## Arquivos principais

- `src/GuiaPlay.Core/MediaTypes.cs`: classificação e roteamento áudio/vídeo.
- `src/GuiaPlay.Core/Playlist.cs`: modelo, catálogo e store atômico.
- `src/GuiaPlay.Core/PlaybackCoordinator.cs`: `CanPlay` e independência das saídas para áudio.
- `src/GuiaPlay.Core/AppSettings.cs`: schema 3.
- `src/GuiaPlay.App/MainWindow.xaml*`: interface operacional, playlist e fluxo imediato.
- `src/GuiaPlay.App/Playback/LibVlcPlaybackEngine.cs`: sessão somente-áudio sem callbacks de vídeo.
- `src/GuiaPlay.App/Windows/ScreenConfigurationWindow.xaml*`: configurações permanentes unificadas.
- `src/GuiaPlay.App/Models/PlaylistTreeNodes.cs` e `Windows/TextPromptWindow.cs`: apresentação leve da playlist.
- `tests/GuiaPlay.Core.Tests/PlaylistTests.cs` e testes ampliados de reprodução/settings.

## Limitações e validações físicas pendentes

- A primeira versão não implementa drag-and-drop/reordenação visual; os campos de ordem já existem.
- Não há thumbnails, waveform, importação física, varredura automática de pastas ou tentativa de localizar arquivo movido.
- A classificação é baseada na extensão; suporte real a codec/container depende do LibVLC 3.0.24.
- Validar visualmente layout, foco, temas, alto contraste e 1366 × 768.
- Validar em hardware áudio sem qualquer alteração visual nas saídas, inclusive com uma e duas telas marcadas.
- Validar vídeo em uma e duas saídas, desconexões/reconexões e operador provisório.
- Ouvir dispositivo padrão e saída USB/HDMI explícita, inclusive desconexão durante reprodução.
- Executar sessão prolongada e registrar CPU/GPU/memória; não declarar 4K ou sincronismo físico perfeito sem teste real.

O roteiro correspondente foi atualizado em `docs/ROTEIRO-VALIDACAO-MANUAL.md`.
