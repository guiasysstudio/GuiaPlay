# GuiaPlay

Protótipo funcional M04 para operar vídeos e áudios locais, organizar/reordenar um cronograma em grupos virtuais e integrar o fluxo com arraste do Explorer, argumentos de linha de comando e instância única.

Versão: **0.4.0-prototipo**  
Plataforma: **Windows x64**  
Interface: **C# + WPF, .NET 10**  
Motor incorporado: **LibVLCSharp 3.10.1 + LibVLC 3.0.24 para Windows**

O pacote `VideoLAN.LibVLC.Windows` leva o motor e os codecs junto com a aplicação. Não é necessário instalar o aplicativo VLC.

## Pré-requisitos

- Windows 10 ou 11 x64;
- .NET SDK 10.0.300 ou patch compatível da linha 10.0 para compilar;
- para executar uma compilação dependente de framework, .NET Desktop Runtime 10 x64;
- monitores configurados no Windows em **Estender estes monitores** para o teste multitelas.

## Compilar e executar

Execute na pasta que contém este README (`E:\Projetos\GuiaSys\GuiaPlay\GuiaPlay`):

```powershell
dotnet restore .\GuiaPlay.slnx
dotnet build .\GuiaPlay.slnx -c Debug
dotnet run --project .\src\GuiaPlay.App\GuiaPlay.App.csproj -c Debug
```

Validação completa de compilação e testes:

```powershell
dotnet test .\GuiaPlay.slnx -c Debug
dotnet build .\GuiaPlay.slnx -c Release
dotnet test .\GuiaPlay.slnx -c Release --no-build
```

O executável Debug fica em `src\GuiaPlay.App\bin\Debug\net10.0-windows\win-x64\GuiaPlay.exe`.

## Uso rápido

1. Abra a engrenagem **Configurações**, dê nomes curtos às telas, escolha exatamente um monitor do operador, aparência e dispositivo de áudio. Salvar aplica o conjunto; Cancelar não altera nada.
2. Use **Identificar telas** e confira os números exibidos por três segundos.
3. Marque uma ou mais saídas públicas. O monitor do operador não pode ser marcado e uma tela reconectada exige nova marcação na sessão.
4. Crie grupos virtuais na **Playlist**, selecione um grupo e adicione referências de mídia. Os arquivos originais não são copiados nem movidos.
   Também é possível arrastar vários arquivos do Explorer para um grupo ou para a área da playlist.
5. Use **Abrir mídia…** ou dê duplo clique em um item da playlist. Arquivos ausentes continuam listados e aparecem como não encontrados.
6. Vídeo exige ao menos uma saída marcada; então **Reproduzir** inicia imediatamente, sem confirmação intermediária. Áudio pode tocar sem saída marcada e nunca abre nem altera janelas de vídeo.
7. Controle pausa/continuação, busca, volume, mudo e parada no painel. Volume, mudo, telas e identificação permanecem na faixa operacional inferior.

Com um único monitor, áudio continua disponível. Vídeo permanece carregado, mas **Reproduzir** fica desabilitado até existir uma saída pública selecionada.

## Documentação

- [Requisitos completos e escopo](docs/REQUISITOS.md)
- [Decisão do motor e arquitetura](docs/DECISAO-MOTOR.md)
- [Roteiro de validação manual](docs/ROTEIRO-VALIDACAO-MANUAL.md)
- [Relatório do M01](docs/RELATORIO-M01.md)
- [Relatório do M01.1](docs/RELATORIO-M01.1.md)
- [Relatório do M02](docs/RELATORIO-M02.md)
- [Relatório do M03](docs/RELATORIO-M03.md)
- [Relatório do M04](docs/RELATORIO-M04.md)
- [Backlog por marcos](docs/BACKLOG.md)

Os logs locais ficam em `%LocalAppData%\GuiaSys\GuiaPlay\GuiaPlay.log`, com rotação a 5 MiB e retenção máxima de cinco arquivos.
As preferências ficam em `%LocalAppData%\GuiaSys\GuiaPlay\settings.json`: aparência, identidade/nome das telas, operador, pré-seleção pública, saída de áudio, volume e mudo. O esquema 3 migra os anteriores, preserva chaves desconhecidas e mantém gravação atômica. A playlist fica separada em `playlist.json` e contém somente metadados leves e caminhos absolutos; nunca contém bytes de mídia.

## Limites conhecidos do protótipo

- A distribuição usa quadros BGRA/RV32 em memória de CPU. Há uma única decodificação e um único relógio LibVLC, mas a cópia CPU e a composição de cada monitor têm custo; não se declara aceleração por GPU.
- TVs e projetores podem adicionar atrasos próprios. Mesma fonte/relógio não significa sincronismo físico perfeito dos painéis.
- A identidade persistente de tela usa o caminho retornado pelo Windows via `QueryDisplayConfig`. Trocar porta, adaptador ou driver pode mudar esse caminho; correspondências ausentes ou ambíguas exigem escolha humana.
- A lista explícita de áudio contém somente pares módulo/dispositivo que o LibVLC informou aceitar. Lista vazia não prova ausência de áudio no Windows; **Padrão do Windows** continua disponível.
- A aplicação pode detectar uma perda de dispositivo depois de um redirecionamento transitório feito pelo backend/Windows. Ela pausa e não faz fallback nem retoma deliberadamente, mas ausência absoluta de transiente requer validação física.
- A classificação inicial de mídia usa extensões comuns; a decodificação efetiva continua sendo responsabilidade do LibVLC e depende do conteúdo/codecs do arquivo.
- Associação automática de formatos, alterações no Registro, instalador e empacotamento ainda não fazem parte desta entrega.

## Argumento de linha de comando e instância única

Um arquivo compatível pode ser carregado sem reprodução automática:

```powershell
.\GuiaPlay.exe "D:\Midias\Abertura.mp4"
```

Se o GuiaPlay já estiver aberto, a nova execução encaminha o caminho por Named Pipe para a janela existente e termina. O M04 não registra associações de arquivos nem altera o player padrão do Windows.

Os ícones incorporados são os SVGs 24 Regular do projeto oficial [Microsoft Fluent UI System Icons](https://github.com/microsoft/fluentui-system-icons), usados sob licença MIT. A licença está em `src/GuiaPlay.App/Assets/Icons/LICENSE-Fluent-System-Icons.txt`.
