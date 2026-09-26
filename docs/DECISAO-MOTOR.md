# Decisão do motor e distribuição de imagem

## Decisão

O M01 usa `LibVLCSharp` **3.10.1** e `VideoLAN.LibVLC.Windows` **3.0.24**, ambos em versões estáveis e explícitas. O pacote nativo é restaurado junto com o projeto e copiado para a saída x64; não depende de uma instalação externa do VLC.

Fontes consultadas:

- [Primeiros passos do LibVLCSharp](https://docs.videolan.me/libvlcsharp/docs/getting_started.html)
- [API de MediaPlayer](https://docs.videolan.me/libvlcsharp/api/LibVLCSharp.Shared.MediaPlayer.html)
- [Pacote LibVLCSharp 3.10.1](https://www.nuget.org/packages/LibVLCSharp/3.10.1)
- [Pacote VideoLAN.LibVLC.Windows 3.0.24](https://www.nuget.org/packages/VideoLAN.LibVLC.Windows/3.0.24)
- [EnumDisplayMonitors](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-enumdisplaymonitors)

## Por que callbacks de vídeo

Um `MediaPlayer` nativo normalmente tem um único destino HWND. Reusar o mesmo player em vários `VideoView` não cria distribuição multitelas. Criar um player por monitor também violaria o requisito de fonte e relógio únicos e introduziria deriva.

O protótipo registra `SetVideoCallbacks` e `SetVideoFormatCallbacks` em **um único MediaPlayer da sessão atual**. O LibVLC converte o quadro para RV32; a aplicação oferece três buffers nativos alinhados a 32 bytes. O callback de exibição publica somente o índice do quadro concluído. A UI copia o quadro mais recente para uma única `WriteableBitmap`, usada simultaneamente por todas as instâncias de `Image`.

```text
arquivo → MediaPlayer/relógio LibVLC → anel de 3 buffers RV32
                                      → WriteableBitmap compartilhada
                                        ├─ prévia do operador
                                        ├─ saída 1
                                        ├─ saída 2
                                        └─ saída N
```

Não há fila sem limite: quadros prontos antigos podem ser descartados quando a UI está atrasada e apenas uma operação de renderização fica pendente no Dispatcher. Os buffers são alocados na negociação de formato e reutilizados; sua vida útil é protegida por estados `Writing`, `Ready` e `Reading`. Não há alocação de buffer de pixels por quadro.

## Custos e medição

A própria documentação do LibVLC alerta que callbacks em memória custam mais do que renderização direta em janela, podem desativar decodificação por hardware ou exigir cópia da GPU e exigem conversão/cópia. Portanto:

- esta implementação é declaradamente uma prova inicial por buffers de CPU;
- não se afirma aceleração por GPU;
- o log mede quadros recebidos, apresentados, coalescidos e tempo médio da cópia `WritePixels` ao fim natural;
- validação de 1080p, 4K e quantidade de saídas precisa ser feita no computador alvo com mídia conhecida.

## Áudio e sincronismo

O áudio vem do mesmo `MediaPlayer` e é emitido uma única vez. No M02, o inventário é obtido diretamente de `LibVLC.AudioOutputs` e `LibVLC.AudioOutputDevices(module)`, e a escolha explícita é aplicada antes de `Play` com `SetAudioOutput(module)` e `SetOutputDevice(deviceId, module)`. Isso preserva exatamente o par de identificadores aceito pelo LibVLCSharp 3.10.1; nomes são apenas apresentação e posição de lista nunca é identidade.

**Padrão do Windows** cria uma nova sessão sem fixar módulo/dispositivo, deixando o backend resolver a rota ao iniciar aquela reprodução. O GuiaPlay não troca deliberadamente a rota quando o padrão muda no meio do vídeo. Uma lista vazia não é tratada como prova de ausência de dispositivos, conforme o contrato da própria API. Saída explícita ausente bloqueia o início em vez de cair silenciosamente no padrão.

A lista é atualizada sob demanda e periodicamente fora de callbacks nativos. O evento `AudioDevice` da sessão e a reconciliação do inventário detectam mudança/perda; a geração da mídia filtra eventos antigos. Ao detectar perda em uso, a aplicação pausa e exige intervenção, sem fallback ou retomada automática. O backend/Windows ainda pode redirecionar transitoriamente antes que o evento chegue, portanto não se promete ausência física absoluta de um trecho em outro destino sem teste no hardware alvo.

As janelas são preparadas antes de `Play`; carregar ou escolher arquivo não decodifica nem libera áudio. Volume e mudo são aplicados ao `MediaPlayer` antes da liberação do áudio, inclusive ao iniciar já em mudo. Todas as imagens vêm do callback de apresentação governado pelo mesmo relógio LibVLC. Isso reduz divergência de software, mas TVs e projetores podem aplicar processamento e latência próprios; sincronismo físico perfeito não é prometido.

## Ciclo de vida

Cada arquivo aceito recebe uma geração monotônica. Um objeto de sessão captura essa geração em todos os eventos. A lógica central ignora eventos de uma geração anterior. Troca e encerramento chamam `Stop` e `Dispose` em tarefa de trabalho, nunca dentro de callback nativo. Ao final/parada/erro, as janelas públicas são fechadas na UI.

## Possível evolução

Se as medições do computador da igreja mostrarem custo excessivo, a alternativa correta é um plug-in de saída de vídeo LibVLC ou um pipeline Direct3D compartilhado com superfícies por swap chain. Migrar é possível porque a integração está isolada em `Playback/LibVlcPlaybackEngine.cs`; o painel depende apenas dos eventos e comandos dessa classe.
