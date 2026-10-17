# GP-I07 — Atalhos de teclado profissionais para o operador

Data: 16/10/2026.  
Branch isolada: `feat/gp-interface-3-modelo1`.  
Situação: **implementado no código; compilação, testes e validação física deliberadamente adiados até GP-I10**.

## Atalhos efetivamente implementados

| Teclas | Comando |
|---|---|
| F1 | Abre ajuda de atalhos (acessível mesmo com licença em verificação ou bloqueada) |
| Espaço | Reproduzir/pausar, quando foco não pertence a um controle interativo |
| F5 | Reproduzir/continuar |
| F6 | Pausar |
| F7 | Parar |
| F8 | Carregar mídia anterior sem iniciar reprodução |
| F9 | Carregar próxima mídia sem iniciar reprodução |
| Ctrl+← | Retroceder 5 segundos |
| Ctrl+→ | Avançar 5 segundos |
| F11 | Abrir prévia somente no monitor do operador (Esc/F11 fecha) |
| Ctrl+O | Abrir um arquivo de vídeo ou áudio |
| Ctrl+Shift+O | Importar pasta com subpastas |
| Ctrl+S | Salvar preset da playlist atual |
| Ctrl+N | Nova playlist, respeitando as confirmações existentes |
| Ctrl+F | Focar a pesquisa de mídias e selecionar texto atual |
| Ctrl+L | Focar a biblioteca de playlists salvas |
| Ctrl+M | Alternar silenciar/ativar áudio |

## Arquitetura

- `OperatorShortcutPolicy` no Core declara os 17 comandos, seus gestos, suas descrições e regras de resolução sem dependências WPF. O mesmo inventário alimenta a documentação exibida dentro do programa.
- `MainWindow_OnPreviewKeyDown` foi ampliado sem remover a lógica anterior de **Esc para fechar as janelas Identificar Monitores**.
- Os atalhos têm **escopo exclusivo da janela principal do GuiaPlay**. Não há global hotkeys, captura ou injeção de teclado em outros processos.
- Atalhos são bloqueados quando houver digitação ou edição em `TextBoxBase`, `PasswordBox` e `ComboBox` (exceto F1, pois não digita caracteres).
- Espaço não interfere no comportamento nativo de `ButtonBase`, `Slider`, `ScrollBar`, `TreeViewItem`, `DataGrid`, `ListBox`, `TabItem` ou `MenuItem` sob foco.
- Rejeitar teclas repetidas seguradas; rejeitar modificadores Alt/Win e combinações não declaradas, inclusive AltGr (`Ctrl+Alt`).
- Bloqueios por licença, operação ocupada e estados `IsEnabled` dos próprios botões são reaproveitados. Ex.: F7 não força parada em botão indisponível; F8/F9 mantêm o comportamento **carregar sem autoplay**; F11 mantém as proteções do monitor do operador.
- Teclas encaminham aos **handlers existentes** de reprodução, importação, salvamento, prévia, busca e mudo. Não existe segundo player nem comandos simulados.
- Componente de ajuda `OperatorShortcutHelpWindow` contém rolagem, gestos e descrição, botão Fechar e Esc; acessível por F1 ou botão **Atalhos** no cabeçalho.
- Tooltips dos comandos principais indicam os atalhos correspondentes, para descoberta sem abrir ajuda.

## Arquivos criados e editados

1. `src/GuiaPlay.Core/OperatorShortcutPolicy.cs` — política de resolução + catálogo de 17 ações.
2. `src/GuiaPlay.App/Windows/OperatorShortcutHelpWindow.cs` — ajuda nativa WPF.
3. `src/GuiaPlay.App/MainWindow.xaml.cs` — foco, modificadores, gating e dispatcher local.
4. `src/GuiaPlay.App/MainWindow.xaml` — botão de ajuda e tooltips acessíveis.
5. `tests/GuiaPlay.Core.Tests/OperatorShortcutPolicyTests.cs` — oito grupos de testes, incluindo teoria de 17 gestos, entradas nativas, Alt/Win, autorepeat e unicidade.
6. `docs/PLANO-INTERFACE-3-MODELO1.md` — progresso.

## Inspeção estática inicial

- Estrutura XAML: **760 tags** com aninhamento completo; não há nomes `x:Name` duplicados, handlers referenciados ausentes ou recursos de tema locais não resolvidos na inspeção.
- Conferida preservação do Esc para identificação de telas e checagens de licença/foco/ocupação antes das ações.
- Conferida existência das 17 descrições correspondentes às 17 ações de operador.
- **Não foi executado** `dotnet restore`, `dotnet build`, `dotnet test` nem smoke de GUI, conforme decisão do usuário de realizar tudo ao concluir as onze etapas.

## Gate da etapa final GP-I10

- Verificar compilação Debug/Release e sucesso do conjunto de testes na máquina Windows.
- Testar os 17 gestos em teclado físico com foco no operador, em tabela, árvore, pesquisa, menu, combo, textbox e janela modal.
- Testar que Espaço continua acionando um botão/check box focado, não iniciando áudio inesperadamente.
- Verificar que F1 abre ajuda mesmo com licença bloqueada e não rouba entradas de texto.
- Validar AltGr, combinações do Windows e teclas seguradas para evitar execução duplicada.
- Testar F8/F9 com mídia pausada/ativa e confirmação de substituição, importação, salvamento, busca e atalhos sob reprodução real e sem monitores.
- Se necessário, abrir etapas GP-I11+ para corrigir achados antes da geração de instalador.

## Próxima etapa

**GP-I08 — YouTube: análise e prova de viabilidade do mecanismo de incorporação oficial.** O controle remoto Android/LAN permanece fora das onze etapas e só será iniciado após validação de todo o GuiaPlay desktop.

**Nenhuma tag, release, instalador, deploy, alteração na branch `main` ou execução de testes neste módulo.**
