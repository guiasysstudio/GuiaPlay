# GP-I06 — Transporte profissional, duplo seek e modos de reprodução

Data-base: 16/10/2026  
Projeto: GuiaPlay (Interface 3.0, referência visual Modelo 1)  
Branch: `feat/gp-interface-3-modelo1`  
Situação: **implementado no código; build, testes automatizados e smoke de monitores pendentes para GP-I10, por decisão do responsável.**

## Entregas

1. **Linha de tempo na Prévia do Operador** e **linha de tempo no transporte inferior**, ambas associadas à posição da mesma sessão do LibVLC. A barra superior recebe o valor/máximo da barra inferior por binding bidirecional, para não existir um segundo relógio/estado paralelo.
2. Arraste do marcador, clique na barra e roda do mouse nas duas timelines. O tempo decorrido/restante é atualizado durante a interação. Ambas chamam o mesmo `TrySeek`, que verifica `_engine.IsSeekable`, duração conhecida, licença, operação ocupada e ciclo válido; entradas impossíveis não prometem seek.
3. Botões **mídia anterior**, **retroceder 5 segundos**, **reproduzir**, **pausar**, **avançar 5 segundos**, **próxima mídia** e **parar**; mudo/volume continuam operando na sessão original (GP-I05 adicionou o segundo controle lateral ligado ao mesmo volume).
4. Anterior e Próxima buscam a ordem real de `PlaylistDocument`, inclusive entre grupos e ignorando filtros de busca visual. **Não iniciam reprodução automaticamente**: preparam a mídia, mantendo a confirmação existente se a reprodução anterior estiver ativa.
5. Estado da música/faixa atual associado ao ID efetivo do item da playlist após carga bem-sucedida. Arquivos abertos diretamente fora da playlist não ativam avanços automáticos sem ID.
6. **Modo Manual (padrão seguro):** no fim natural da mídia, fecha a janela pública como antes e não inicia o próximo item. Esse padrão é aplicado em cada inicialização, sem migrar schema de preferências.
7. **Modo Sequencial (habilitado expressamente):** ao término natural, tenta carregar e iniciar a próxima mídia; se houver fim de playlist, indisponibilidade, falha, alteração de playlist, revogação de licença ou mudança de modo, interrompe a cadeia. Jamais pula mídia indisponível automaticamente.
8. **Repetir:** desativado, repetir a mídia atual ou repetir a playlist. Só provoca reprodução automática quando Sequencial estiver ativo. Repetir Atual tem prioridade sobre sorteio aleatório.
9. **Aleatório:** seleciona dentre mídias ainda não visitadas no ciclo, sem repetir a mídia atual; com Repetir desativado, encerra o ciclo quando esgotadas. Com Repetir Lista, abre novo ciclo. A seleção visual de uma nova faixa reinicia o histórico do ciclo.
10. Próxima mídia exibida como texto real, ou identificada como escolha aleatória quando Shuffle está habilitado. Sinaliza a suspensão da sequência após mudança da playlist durante reprodução.
11. Nova política pura `PlaylistPlaybackNavigation` controla anterior/próxima, manual/sequencial, repetir atual/lista e ordenação, com testes xUnit escritos e ainda não executados.
12. `LibVlcPlaybackEngine.IsSeekable` expõe o estado real do player, evitando barras habilitadas para arquivos/formatos que não permitem posicionamento.
13. `_mediaLoadRequestId` invalida leituras assíncronas antigas quando o operador faz uma nova escolha; revogação do modo sequencial durante uma sondagem cancela uma transição automática pendente.
14. Parada explícita tem guarda `_suppressAutoAdvance` para nunca ser confundida com fim natural. Avanço sequencial só é considerado bem-sucedido se o coordenador estiver preparando ou reproduzindo de fato.

## Arquivos

- `src/GuiaPlay.App/MainWindow.xaml`: nova barra na prévia, botões de navegação/seek, alternância Manual/Sequencial, Repetir e Aleatório, resumo da próxima mídia.
- `src/GuiaPlay.App/MainWindow.xaml.cs`: eventos e estados reais, sincronismo de timelines, alteração de modos, prevenção de autoplay indevido, transição no término de mídia e restauração de controles.
- `src/GuiaPlay.App/Playback/LibVlcPlaybackEngine.cs`: propriedade somente leitura `IsSeekable`, protegida pela trava de sessão nativa.
- `src/GuiaPlay.Core/PlaylistPlaybackNavigation.cs`: política sem dependência WPF para seleção das faixas.
- `tests/GuiaPlay.Core.Tests/PlaylistPlaybackNavigationTests.cs`: oito testes de política preparados, sem execução.
- `docs/PLANO-INTERFACE-3-MODELO1.md`: progresso atualizado.

## Inspeção estática

- Verificados aninhamento de XAML, `x:Name` únicos, recursos de tema e existência dos handlers declarados.
- Não alterados o protocolo do GuiaSys Licensing, as políticas de segurança dos monitores, o roteamento nativo de áudio, o atualizador ou os arquivos de mídia dos usuários.
- Não foram executados `dotnet build`, `dotnet test`, UI nem testes físicos, conforme instrução expressa de concluir GP-I00 a GP-I10 primeiro.
- Não criado instalador, release, tag nem commit em `main`.

## Gates no GP-I10 (não executados nesta etapa)

- Compilação Debug/Release e testes x64/x86 da solução.
- Seek em vídeo local, áudio, arquivo curto, mídia sem seek, durante pausa e com atraso de duração.
- Comparar as duas timelines ao arrastar/soltar, por clique e por roda, inclusive com DPI diferente.
- Regressão do modo manual: jamais avançar automaticamente.
- Sequencial: vídeo→vídeo, áudio→vídeo, troca de grupos, última faixa, repetição atual/lista, shuffle sem repetição, falha de mídia, saída desconectada e licença revogada.
- Verificar se alterações da playlist interrompem autoplay seguro e se uma nova carga solicitada pelo operador cancela a fila assíncrona anterior.
- Testar parar manualmente enquanto uma transição está em andamento; procurar qualquer evento de fim nativo atrasado.
- Conferir UI mínima 1366×768, 100%, 125% e 150% de escala, mantendo botões clicáveis e legíveis.

## Próxima

GP-I07: atalhos do operador, acessibilidade e conflitos com campos de texto; integrar os atalhos às mesmas ações, sem criar motor de playback duplicado.
