# Relatório de implementação — M04

Data: 25/09/2026  
Versão: 0.4.0-prototipo

## Resultado

O M04 fecha o checkpoint visual do M03 e acrescenta o fluxo operacional com o Windows sem alterar o pipeline validado de telas, LibVLC, áudio/vídeo ou persistência. A playlist continua armazenando apenas metadados e caminhos absolutos para os arquivos originais.

## Correções visuais

O diálogo **Novo grupo/Renomear grupo** não possui mais altura fixa. Ele usa linhas `Auto`, `SizeToContent=Height`, margens internas e layout rounding, evitando o corte dos botões em DPI elevado. O campo recebe foco e seleção inicial; Enter confirma, Esc cancela e OK acompanha a validação de nome não vazio com até 80 caracteres.

Os demais diálogos próprios foram revisados: configurações é redimensionável e possui conteúdo rolável; identificadores têm tamanho fixo intencional e não contêm botões. MessageBox permanece responsabilidade do Windows.

O antigo ComboBox do operador foi removido da tela principal. Em seu lugar há um badge estático, sem seta, lista ou seleção. Estado normal mostra o nome curto; operador salvo ausente/configuração provisória usa texto e borda de atenção discretos. A escolha continua exclusiva das Configurações.

A janela passou a iniciar em 1040 × 560 DIPs, com mínimo 800 × 450 e ajuste à área útil do Windows. Margens, truncamento, botões e estados desabilitados receberam revisão preservando prévia secundária, playlist relevante e faixa operacional acessível.

## Ícones

Foram incorporados somente dez SVGs 24 Regular do repositório oficial [Microsoft Fluent UI System Icons](https://github.com/microsoft/fluentui-system-icons):

- Settings;
- Folder Add;
- Document Add;
- Edit;
- Delete;
- Play;
- Pause;
- Stop;
- Speaker 2;
- Speaker Mute.

Os SVGs originais ficam em `src/GuiaPlay.App/Assets/Icons/` e são recursos incorporados. Seus paths também foram convertidos para `Geometry` no XAML, usando `Path` nativo do WPF e a cor do próprio botão. Não foi adicionada biblioteca de SVG nem dependência em internet durante execução.

Origem: `microsoft/fluentui-system-icons`, copyright Microsoft Corporation, licença MIT. A cópia da licença oficial está em `Assets/Icons/LICENSE-Fluent-System-Icons.txt` e é levada ao diretório de saída.

## Drag-and-drop e organização

Arquivos arrastados do Explorer são recebidos por `DataFormats.FileDrop`. O classificador existente aceita apenas caminhos presentes e extensões conhecidas; vários arquivos podem ser tratados juntos e rejeições aparecem apenas no status.

Destino do drop:

- sobre grupo ou item: usa o respectivo grupo;
- área neutra: usa o grupo selecionado ou o primeiro grupo;
- playlist vazia: cria o grupo virtual **Mídias**.

`PlaylistCatalog.AddItems` cadastra referências sem ler conteúdo, copiar ou mover arquivos. O drag interno permite reordenar grupos, reordenar itens e mover item entre grupos. Somente os arrays/índices de metadados mudam; `OriginalPath` e arquivo físico permanecem intactos. A gravação continua atômica e o schema 1 já possuía IDs/ordem, portanto não exigiu migração.

## Duplo clique

`PlaylistActivationPolicy` formaliza o fluxo:

- vídeo disponível com saída: carrega e reproduz;
- vídeo disponível sem saída: apenas carrega, sem popup;
- áudio disponível: carrega e reproduz independentemente das telas;
- item ausente/desconhecido: não tenta carregar ou reproduzir.

A execução ainda passa por `LoadMediaAsync`, `PlaybackCoordinator.CanPlay` e `PlaybackRouting`, preservando todas as garantias do M03.

## Argumentos e instância única

`LaunchArgumentParser` normaliza o primeiro caminho, confirma existência e classifica o formato. Um argumento válido carrega a mídia após a janela abrir, sem autoplay. Caminho ausente/inválido ou formato não suportado gera aviso discreto e mantém o programa utilizável.

`SingleInstanceCoordinator` usa um Mutex nomeado por usuário/sessão e Named Pipe assíncrono com `CurrentUserOnly`. A instância primária mantém um listener bloqueante sem polling. Uma execução seguinte encaminha caminho/solicitação de ativação, termina com código 0 e não inicializa settings, playlist ou LibVLC próprios. A janela existente restaura estado minimizado, vem à frente quando permitido pelo Windows e carrega o arquivo recebido sem perder configurações/playlist.

Não foram criados associação de formatos, alterações no Registro, player padrão, instalador ou publicação.

## Performance

- Nenhuma dependência nova foi adicionada.
- Não há banco, servidor web, thumbnails, cache de mídia ou leitura integral de arquivos.
- Drag externo trabalha com strings fornecidas pelo Windows.
- Drag interno altera listas pequenas de metadados.
- Named Pipe espera eventos; não há polling de instância.
- Ícones usam Geometry/Path nativo e recursos locais pequenos.

## Testes e validação

- Testes existentes preservados: 45/45.
- Total após M04: 64 testes.
- Build Debug padrão: aprovado, 0 erros e 0 avisos.
- Testes Debug: 64/64 aprovados.
- Build Release: aprovado, 0 erros e 0 avisos.
- Testes Release: 64/64 aprovados.
- `dotnet format --verify-no-changes`: aprovado.
- Smoke Release com settings/playlist temporários: janela `GuiaPlay 0.4.0-prototipo` criada e responsiva.
- Smoke de argumento: processo primário recebeu caminho de áudio suportado sem falha de inicialização.
- Smoke de instância única: segunda execução encaminhou a solicitação, terminou com código 0 e permaneceu exatamente um processo Release do M04.
- Dados temporários e build isolado foram removidos; `settings.json` real não foi modificado.

Cobertura nova inclui validação de nome, operador estático/desconectado, importação múltipla e rejeição, preservação de arquivos, reordenação/movimentação/persistência, política de duplo clique, argumentos válidos/inválidos e encaminhamento real por Named Pipe.

## Limitações e validação manual pendente

- O conector de inspeção visual retornou `apps: []`; não foi possível produzir aprovação visual automatizada do diálogo, ícones ou temas.
- Andrew deve validar o diálogo em 100%, 125% e 150%, especialmente 1366 × 768, Claro/Escuro e alto contraste.
- Validar visualmente drop targets e a experiência de reordenação com listas longas.
- Validar ativação da janela por segunda instância em diferentes estados de foco; o Windows pode restringir roubo de foco em alguns cenários.
- Validar Explorer com caminhos de rede, unidades removíveis e arquivos que desaparecem durante o drop.
- Revalidar áudio/vídeo e desconexões no hardware físico conforme o roteiro.

O roteiro atualizado está em `docs/ROTEIRO-VALIDACAO-MANUAL.md`.
