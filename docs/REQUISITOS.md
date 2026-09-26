# Requisitos do produto GuiaPlay

Este documento registra o produto completo e o histórico dos marcos implementados.

## Visão do produto

- Nome GuiaPlay, interface em português, arquivos locais e funcionamento offline.
- Detectar telas, identificá-las visualmente, permitir nomes personalizados, escolher a tela do operador e salvar a configuração.
- Vídeo só pode iniciar quando houver saída pública selecionada; **Reproduzir** inicia imediatamente, sem diálogo intermediário.
- Áudio independe da seleção de telas e nunca abre, limpa ou altera uma saída de vídeo.
- Abrir janela sem bordas em cada saída selecionada, preservar proporção sem esticar ou cortar e não exibir controles, título ou diálogos nas saídas.
- Painel do operador com prévia, abrir, reproduzir, pausar, parar, volume, silenciar, progresso arrastável, tempos decorrido/restante/total e saídas ativas.
- Emitir um único áudio e permitir escolha do dispositivo nas configurações.
- No fim natural, fechar todas as janelas públicas, revelar o conteúdo anterior e manter o painel aberto; não deixar janela preta, último quadro ou janela vazia.
- **Parar** interrompe, volta ao início, fecha saídas e mantém o arquivo carregado.
- **Pausar** mantém o quadro nas saídas; **Continuar** retoma da mesma posição.
- Ao substituir um vídeo ativo, confirmar antes de interromper; cancelar preserva a reprodução.
- Desconexão não pode fazer o vídeo aparecer no monitor do operador.
- Produto completo: abertura pelo Explorer após associação feita pelo usuário, arrastar arquivos e encaminhar novas aberturas à instância existente.
- Organizar referências de mídia em playlist com grupos virtuais, sem copiar, mover ou incorporar os arquivos originais.
- Fora de escopo inicial: streaming, YouTube, letras, contas, nuvem, licenciamento e captura de outras aplicações.

## M01 — implementado

- Solução na raiz com aplicação WPF, lógica testável, testes e documentação.
- Enumeração de todos os monitores ativos por Win32, exibindo dispositivo, resolução, posição virtual, escala e indicação de principal.
- Escolha explícita do operador; exclusão desse monitor da lista de saídas; seleção de uma ou mais saídas; identificação numérica temporária.
- Janelas públicas sem bordas, sem controles, no retângulo físico integral de cada monitor por `SetWindowPos`; coordenadas negativas e DPI por monitor são preservados sem mudar a configuração do Windows.
- Modo de um monitor identificado como teste apenas da prévia.
- Abertura de arquivo local e confirmação de destinos antes de tocar.
- Uma sessão LibVLC, um relógio, uma decodificação e distribuição dos quadros em memória para uma imagem WPF compartilhada por prévia e saídas.
- Play, pausa, continuar, parar, volume 0–100%, mudo, tempos reais e seek sem atualização concorrente durante o arraste.
- Áudio único pelo dispositivo padrão do Windows.
- Fim natural, parada, pausa, repetição e troca confirmada.
- Gerações de mídia impedem que evento atrasado da sessão anterior altere a atual.
- Eventos nativos são encaminhados ao Dispatcher; `Stop`/`Dispose` bloqueantes são executados fora da thread de callback e fora da UI.
- Desconexão: todas as saídas são ocultadas ao receber `WM_DISPLAYCHANGE`; as válidas são reposicionadas, as ausentes são removidas e, se não restar saída pública, a reprodução pausa.
- Falhas são informadas no operador e registradas; fechamento do painel encerra motor, áudio e janelas.
- Logs locais limitados por tamanho e retenção.

## Fora do M01

Consulte [BACKLOG.md](BACKLOG.md). Em especial, M01 não persiste preferências, não nomeia telas, não escolhe dispositivo de áudio, não cria associação de arquivo e não fornece instalador.

## M01.1 — correção de interface

- Aparência Sistema/Claro/Escuro com troca imediata e persistência somente dessa preferência.
- Tema Sistema acompanha mudanças do Windows; alto contraste usa os recursos próprios do WPF.
- Controles preservam estados habilitado/desabilitado e adotam templates Fluent com contraste coerente.
- Rótulos de telas usam linha principal curta e detalhes de resolução/posição/escala em linha secundária e tooltip.
- Identificação usa quadrado de 112 DIPs no canto inferior esquerdo da área útil de cada monitor, sem janela de tela cheia.
- O M01.1 não altera o pipeline LibVLC nem antecipa as demais configurações do M02.

## M02 — configurações persistentes e saída de áudio

- Um único `settings.json` versionado preserva aparência e armazena operador, nomes, pré-seleção pública, áudio, volume e mudo.
- Identidade persistente usa o caminho de dispositivo do alvo retornado pelas APIs CCD do Windows; correspondência ausente ou ambígua exige nova escolha.
- **Configurar telas…** aplica nomes e operador de forma atômica por Salvar/Cancelar e somente com reprodução parada.
- Operador ausente usa tela principal provisória sem sobrescrever a preferência; sua perda durante reprodução pausa e exige reconfiguração.
- Saída pública reconectada não recupera autorização durante a mesma sessão sem nova seleção explícita.
- O seletor de áudio oferece Padrão do Windows e pares módulo/dispositivo enumerados pelo LibVLC; saída explícita ausente não cai silenciosamente no padrão.
- Saída de áudio perdida pausa a geração atual, sem retomada ou fallback deliberado. Volume e mudo permanecem disponíveis.
- O M02 mantém uma única sessão, relógio, decodificação e emissão de áudio.

## M03 — interface do operador e playlist de mídia

- Configurações permanentes saem da lateral e ficam atrás da engrenagem; Salvar/Cancelar continuam atômicos para o usuário.
- A janela principal prioriza saídas e cronograma: prévia menor, playlist à direita e controles imediatos embaixo.
- Playlist separada em `playlist.json`, contendo somente IDs, nomes, ordens, tipo e caminho absoluto original.
- Grupos são virtuais; adicionar/remover referências e excluir grupos nunca copia, move ou apaga mídia.
- Arquivo original ausente permanece cadastrado e aparece como indisponível.
- Formatos comuns de vídeo e áudio são classificados pela extensão e entregues ao LibVLC, sem codecs próprios.
- `CanPlay` centralizado: vídeo requer saída; áudio requer apenas mídia válida; ausência de mídia bloqueia.
- Áudio usa sessão sem vídeo, não cria `OutputWindow` e não instala callbacks de quadros.
- Reprodução e duplo clique não mostram confirmação intermediária.
- Configurações evoluem para schema 3; playlist usa schema 1 e gravação atômica separada.

## M04 — fluxo operacional e integração com Windows

- Diálogos pequenos usam dimensionamento automático; Enter/Esc e validação do nome seguem o comportamento padrão.
- A tela principal apresenta o operador como indicador estático, inclusive quando a configuração salva está desconectada.
- Ícones Microsoft Fluent System Icons 24 Regular são incorporados localmente como SVG e Geometry XAML, sob licença MIT.
- Arquivos do Explorer podem ser arrastados em lote para a playlist; somente caminhos reconhecidos são cadastrados.
- Grupos e itens podem ser reordenados por arraste, e itens podem mudar de grupo sem tocar no arquivo físico.
- Um caminho de mídia pode ser recebido por argumento, carregando sem reprodução automática.
- Mutex + Named Pipe garantem uma instância persistente; execuções seguintes encaminham o argumento e terminam.
- O M04 não cria associação de formatos, não altera o Registro, não define player padrão e não cria instalador.
