# GP-I05 — Prévia, Programa, monitores e áudio

Data: 16/10/2026
Repositório: `guiasysstudio/GuiaPlay`
Branch: `feat/gp-interface-3-modelo1`
Status: **implementado no código, não compilado, não executado e não aprovado no Windows** (regra do usuário: concluir GP-I00 a GP-I10 antes dos testes integrados).

## Entregáveis

1. **Painéis Prévia do Operador e Programa / Tela Pública** construídos lado a lado, abaixo do nome da mídia em reprodução, seguindo a composição do Modelo 1.
2. As duas janelas visuais usam **um único `WriteableBitmap`**, gerado pelo mesmo LibVLC e compartilhado com as janelas `OutputWindow` existentes. Nenhum decodificador adicional nem cópia de quadros por cada monitor foi introduzido nesta etapa.
3. **Indicador Programa** deriva de dados reais: tipo de mídia, estado de `PlaybackCoordinator`, quantidade de janelas públicas efetivamente abertas e disponibilidade do primeiro quadro. Exibe `SEM TRANSMISSÃO`, `PREPARANDO`, `AO VIVO`, `PAUSADO` e `SOMENTE ÁUDIO` conforme o caso; não confunde *monitor selecionado* com *janela pública ativa*.
4. A prévia de Programa é um **espelho lógico do quadro enviado ao GuiaPlay**, **não uma captura física** do projetor/TV. Ela não afirma comprovar por si só que o monitor externo esteja exibindo a imagem.
5. O quadro do Programa só aparece quando há saída pública de vídeo ativa com quadro recebido. Enquanto não há saída, há instrução clara para selecionar uma tela e iniciar um vídeo.
6. Painel direito **TELAS E ÁUDIO**: identificação da tela do operador, aviso explícito de exclusividade, seletores dos monitores públicos reais, contador de saídas escolhidas, lista separada de janelas públicas efetivamente abertas, avisos reais da topologia, botão Identificar e acesso à configuração.
7. O monitor do operador **continua excluído do conjunto de monitores públicos** via `RebuildOutputChoices`, reaproveitando a proteção existente. Os seletores públicos ficam inativos durante playback para que o estado `selecionado` não seja confundido com reencaminhamento de saída em tempo real.
8. O painel de áudio exibe **o dispositivo persistido** (`AudioOutputPreference`), estado de disponibilidade e alertas reais já derivados do LibVLC. O botão `Selecionar dispositivo de áudio…` navega diretamente à guia Áudio na janela de configurações existente.
9. A seleção de um dispositivo diferente **não é aplicada durante reprodução ativa**; permanece a lógica pré-existente de exigência de parada para roteamento seguro, sem fallback silencioso.
10. Volume adicional na coluna direita vinculado em **TwoWay** ao slider inferior já existente; botão Silenciar/Ativar som encaminha ao handler original `MuteButton_OnClick`, preservando o mesmo volume, configuração e mudo da sessão.
11. A coluna direita é verticalmente rolável para acomodar resoluções menores sem esconder os controles de configuração na parte inferior.
12. Status de saída é recalculado em início/fechamento de janelas públicas, eventos de reprodução, alteração de escolhas e chegada do primeiro quadro. Não há taxa extra de renderização da prévia da Tela Pública por frame.

## Arquivos de código

- `src/GuiaPlay.App/MainWindow.xaml`: previews reais, placeholders de Programa, sidebar vídeo/áudio, scroll e controle de volume compartilhado.
- `src/GuiaPlay.App/MainWindow.xaml.cs`: estados reais do Programa, atualização sincronizada, proteção de seleção durante playback, mensagens da tela do operador e encaminhamento à configuração de áudio.
- `src/GuiaPlay.App/Windows/ScreenConfigurationWindow.xaml.cs`: opção `openAudio` para selecionar diretamente a guia áudio, reaproveitando as políticas existentes.
- `src/GuiaPlay.Core/ProgramStagePresentation.cs`: política pura de estados para interface.
- `tests/GuiaPlay.Core.Tests/ProgramStagePresentationPolicyTests.cs`: cinco grupos de testes **escritos, ainda não executados**.
- `docs/PLANO-INTERFACE-3-MODELO1.md`: status.

## Inspeção estática registrada

- Inspecionadas 718 tags XAML quanto a estrutura/aninhamento; nenhum `x:Name` duplicado, nem handler de evento ou recurso local ausente foi encontrado.
- Conferido que `ProgramImage.Source` recebe o mesmo `_bitmap` que a prévia do operador/janelas de saída, somente sob condição de saída pública existente e quadro disponível.
- Conferido que `SidebarOutputsList` e `OutputsList` ficam desabilitados durante sessão ativa, e que `_monitors.Where(... != _operatorMonitor.Id)` permanece como fonte de escolhas públicas.
- Conferidos estado do áudio, controle de volume compartilhado, separação entre monitores selecionados e efetivamente abertos e navegação direta à guia Áudio.

## Validação obrigatória ao concluir GP-I10

1. Compilar Debug e Release, executar testes de Core e App para x64/x86.
2. Testar vídeo com operador + 1 ou 2 monitores, verificar sincronismo da prévia e da janela pública, e se o terceiro painel não aumenta decodificadores ativos.
3. Testar tempo de início, pausa, fim natural, parada, erro, perda/reconexão de monitor e mudança de resolução/DPI.
4. Verificar que seleção de monitores durante reprodução permanece bloqueada sem interferir no vídeo em execução.
5. Testar áudio puro, saída padrão, dispositivo explícito, dispositivo desconectado, salvamento, mute e volume na barra inferior e no painel direito.
6. Validar layout 1366×768 e 1920×1080, escalas 100%, 125%, 150%, acessibilidade e rolagem do painel direito.
7. Corrigir eventuais problemas descobertos em etapas GP-I11+ antes de criar instalador ou Release.

**Nenhum instalador, tag, publicação, release ou alteração da branch `main`.**
