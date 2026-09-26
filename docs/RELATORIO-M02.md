# Relatório de implementação — M02

Data: 25/09/2026  
Versão: 0.2.0-prototipo

## Resultado

O M02 amplia o protótipo sem substituir o motor, o relógio, os callbacks de vídeo ou o buffer compartilhado. O GuiaPlay agora mantém configuração persistente das telas, nomes personalizados, operador único, pré-seleção pública, saída real de áudio, volume, mudo e aparência.

O retorno anterior de Andrew — reprodução funcionando normalmente e interface “ficando boa” — foi usado como autorização para avançar. Ele continua registrado como relato manual, não como medição de desempenho, prova de sincronismo físico ou cobertura completa do M02.

## Persistência

`AppSettingsStore` usa o mesmo `%LocalAppData%\GuiaSys\GuiaPlay\settings.json`, agora com `schemaVersion` 2. O carregamento aceita o arquivo do M01.1 contendo apenas `appearance`, valida enumerações/tipos, limita volume a 0–100 e usa padrões seguros para campos ausentes.

As propriedades desconhecidas da raiz e dos objetos conhecidos são mantidas. A gravação serializa para arquivo temporário no mesmo diretório, força flush e substitui o destino. Falhas retornam resultado não fatal, aparecem discretamente no painel e vão para o log. JSON inválido é copiado como `settings.invalid-*.json`; somente esse padrão é podado e a retenção máxima é três cópias.

O slider usa debounce de 650 ms e há flush no encerramento. Atualizações automáticas do inventário não salvam escolhas arbitrárias: a preferência pública muda somente quando o usuário marca/desmarca, e uma desconexão não apaga a última escolha explícita persistida.

## Identidade e configuração das telas

`MonitorService` combina `EnumDisplayMonitors` com `QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS)` e duas consultas `DisplayConfigGetDeviceInfo`: nome GDI da origem para correlacionar a tela da sessão e `monitorDevicePath` do alvo como identidade persistente. HMONITOR, `DISPLAYn`, número, resolução, posição e DPI permanecem dados transitórios.

Uma identidade só é confiável quando o caminho é único na topologia atual. Caminho ausente, mais de um alvo para a mesma origem ou caminho duplicado entre telas é marcado ambíguo; nesse caso nome/função/saída não são restaurados automaticamente. Não há tentativa por modelo ou nome amigável. Troca de porta, adaptador ou driver pode mudar o caminho, portanto a identidade não é apresentada como eterna.

A janela **Configurar telas…** mantém as edições isoladas até Salvar, aceita nomes Unicode de até 40 caracteres, remove espaços externos e usa `Tela N` quando vazio. Nomes repetidos continuam acompanhados do número. Nomes de dispositivos desconectados permanecem no JSON e aparecem como conhecidos ausentes. O operador é único, sai das opções públicas e a troca só é possível com reprodução parada. O painel é reposicionado por pixels físicos dentro da área útil, inclusive com coordenadas negativas e escala DPI.

Na ausência do operador salvo, a tela principal vira operador provisório sem sobrescrever a preferência. Reprodução só na prévia continua possível; reprodução pública exige nova escolha. Se o operador some durante o vídeo, o coordenador pausa, preserva mídia/posição, move o painel, fecha eventual saída no operador provisório e bloqueia retomada pública até Parar e reconfigurar.

Saídas salvas são apenas pré-seleção no início. Somente identidades exatas e conectadas são restauradas, sempre excluindo o operador. Uma tela que desconecta perde a autorização da sessão e volta desmarcada, mesmo que sua escolha permaneça no cadastro para a próxima inicialização.

## Saída de áudio

A implementação foi conferida contra a DLL instalada `LibVLCSharp 3.10.1`, inclusive por reflexão dos nomes e ordem dos parâmetros. O inventário usa `LibVLC.AudioOutputs` e `LibVLC.AudioOutputDevices(module)`; cada item persiste módulo, identificador real e nome separado. O seletor sempre inclui **Padrão do Windows**. Lista vazia gera aviso honesto, não uma falsa conclusão de que o Windows não tem áudio.

Antes de cada novo `Play`, uma única sessão `MediaPlayer` é recriada parada. Saída explícita usa `SetAudioOutput(module)` e `SetOutputDevice(deviceId, module)`; volume e mudo são aplicados antes de `Play`. O modo padrão não fixa identificador e é resolvido pelo backend naquela sessão. A interface não cria player de áudio adicional e o seletor fica bloqueado durante reprodução ou pausa; volume e mudo continuam ativos.

Dispositivo explícito salvo e ausente continua visível como indisponível e bloqueia o início. Inventário periódico e o evento `AudioDevice`, ambos associados à geração atual, detectam perda/mudança. O vídeo pausa na posição atual, sem fallback ou retomada deliberada. Eventos de gerações antigas são ignorados. Um redirecionamento transitório do Windows/LibVLC pode ocorrer antes da detecção; somente teste físico pode caracterizar esse intervalo.

## Arquivos principais

- `src/GuiaPlay.Core/AppSettings.cs`: esquema, migração, validação, backup e gravação segura.
- `src/GuiaPlay.Core/DeviceSelection.cs`: reconciliação testável de telas e áudio.
- `src/GuiaPlay.Core/PlaybackCoordinator.cs`: perda de dispositivo obrigatório com filtro de geração.
- `src/GuiaPlay.App/Services/MonitorService.cs`: identidade CCD do Windows.
- `src/GuiaPlay.App/Windows/ScreenConfigurationWindow.*`: edição atômica de nomes e operador.
- `src/GuiaPlay.App/Playback/LibVlcPlaybackEngine.cs`: inventário e roteamento LibVLC.
- `src/GuiaPlay.App/MainWindow.*`: restauração, UX, desconexões, persistência e bloqueios.
- testes, README, backlog, decisão do motor e roteiro manual.

## Verificações executadas

- Build Debug: aprovado sem avisos ou erros.
- Testes Debug: 24/24 aprovados.
- Build Release: aprovado sem avisos ou erros.
- Testes Release: 24/24 aprovados.
- `dotnet format --verify-no-changes`: aprovado.
- Smoke Debug com `settings.json` temporário: janela `GuiaPlay 0.2.0-prototipo` criada, processo responsivo e enumeração real de telas/áudio sem falha de inicialização.
- Smoke Release com `settings.json` temporário: janela `GuiaPlay 0.2.0-prototipo` criada e responsiva; assembly/file version `0.2.0.0` e product version `0.2.0-prototipo`.
- O conector visual retornou inventário vazio (`apps: []`); o smoke de processo não é apresentado como inspeção visual.
- Nenhum vídeo pessoal foi procurado e nenhuma configuração global do Windows foi alterada.

## Pendências reais para Andrew

- Conferir visualmente layout, foco, teclado, temas/alto contraste e 1366 × 768.
- Validar nomes e identidade ao reordenar, desconectar e trocar fisicamente portas das telas.
- Ouvir **Padrão do Windows** e uma saída USB/HDMI explícita, incluindo início em mudo e vídeo sem trilha.
- Desconectar/reconectar a saída de áudio durante reprodução e observar eventual transiente anterior à pausa.
- Executar o roteiro com uma e duas saídas públicas, incluindo operador ausente, pausa, seek, parada e fim natural.

## Executar

Na raiz aberta:

```powershell
dotnet run --project .\src\GuiaPlay.App\GuiaPlay.App.csproj -c Release
```

## Referências oficiais conferidas

- [LibVLCSharp MediaPlayer](https://docs.videolan.me/libvlcsharp/api/LibVLCSharp.Shared.MediaPlayer.html)
- [QueryDisplayConfig](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-querydisplayconfig)
- [DISPLAYCONFIG_TARGET_DEVICE_NAME](https://learn.microsoft.com/windows/win32/api/wingdi/ns-wingdi-displayconfig_target_device_name)
- [DISPLAYCONFIG_SOURCE_DEVICE_NAME](https://learn.microsoft.com/windows/win32/api/wingdi/ns-wingdi-displayconfig_source_device_name)
