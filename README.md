# GuiaPlay

Protótipo funcional M08 para operar vídeos e áudios locais, organizar/reordenar um cronograma em grupos virtuais, distribuir atualizações verificadas e integrar opcionalmente o GuiaPlay ao Windows Explorer.

Versão: **0.8.0-prototipo**

Plataforma: **Windows x64**

Interface: **C# + WPF, .NET 10**

Motor incorporado: **LibVLCSharp 3.10.1 + LibVLC 3.0.24 para Windows**

O pacote `VideoLAN.LibVLC.Windows` leva o motor e os codecs junto com a aplicação. Não é necessário instalar o aplicativo VLC.

## Pré-requisitos

- Windows 10 ou 11 x64;
- .NET SDK 10.0.300 ou patch compatível da linha 10.0 para compilar;
- para executar uma compilação dependente de framework, .NET Desktop Runtime 10 x64;
- monitores configurados no Windows em **Estender estes monitores** para o teste multitelas.
- Inno Setup 6 (`JRSoftware.InnoSetup`) para gerar o instalador.

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

Para montar o publish self-contained win-x64, ZIP, manifesto, checksums e Setup:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-release.ps1
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
7. Controle pausa/continuação, busca, volume, mudo e parada no painel. A roda do mouse ajusta volume em passos de 5 pontos e avança/retrocede a timeline em 5 segundos; um clique direto na barra faz seek proporcional.
8. Em **Configurações > Integração com Windows**, escolha separadamente se o GuiaPlay deve aparecer em **Abrir com** e se o Explorer deve mostrar **Abrir com GuiaPlay**. Nenhuma opção o torna player padrão automaticamente.

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
- [Relatório do M05](docs/RELATORIO-M05.md)
- [Relatório do M06](docs/RELATORIO-M06.md)
- [Relatório do M07](docs/RELATORIO-M07.md)
- [Relatório do M08](docs/RELATORIO-M08.md)
- [Notas da versão 0.5.0-prototipo](docs/releases/0.5.0-prototipo.md)
- [Notas da versão 0.6.0-prototipo](docs/releases/0.6.0-prototipo.md)
- [Notas da versão 0.7.0-prototipo](docs/releases/0.7.0-prototipo.md)
- [Notas da versão 0.8.0-prototipo](docs/releases/0.8.0-prototipo.md)
- [Guia dos assets oficiais](docs/branding/README-COMO-USAR.md)
- [Backlog por marcos](docs/BACKLOG.md)

Os logs locais ficam em `%LocalAppData%\GuiaSys\GuiaPlay\GuiaPlay.log`, com rotação a 5 MiB e retenção máxima de cinco arquivos.
As preferências ficam em `%LocalAppData%\GuiaSys\GuiaPlay\settings.json`: aparência, identidade/nome das telas, operador, pré-seleção pública, saída de áudio, volume, mudo, política de atualização e o cache leve da última consulta. O esquema 5 migra os anteriores, preserva chaves desconhecidas e mantém gravação atômica. A playlist fica separada em `playlist.json` e contém somente metadados leves e caminhos absolutos; nunca contém bytes de mídia. Esses dados ficam fora de `%LocalAppData%\Programs\GuiaPlay` e não são substituídos pelo updater.

## Atualizações e distribuição

O canal interno `Prototype` consulta assincronamente a lista de Releases de `guiasysstudio/GuiaPlay`, incluindo prereleases compatíveis e ignorando drafts/tags inválidas. A checagem automática é ativada por padrão e limitada a uma tentativa a cada 12 horas. O resultado leve da última consulta é persistido, a seta pode ser restaurada imediatamente em um novo processo e a consulta é forçada quando a versão instalada mudou ou o cache está inválido. A instalação automática começa desativada e nunca interrompe mídia ativa.

Em **Configurações > Atualizações** ficam a versão/data local, busca manual e ação de instalação. Uma seta aparece à esquerda da engrenagem somente quando há uma versão mais nova. O download mostra bytes e percentual reais quando `Content-Length` existe, usa estado indeterminado sem tamanho conhecido e distingue download, SHA-256, preparação e início do updater. Download e instalação exigem `update-manifest.json`, SHA-256 válido e o marcador `install.json` criado pelo Setup. Execuções via `dotnet run`, `bin/Debug`, `bin/Release` ou pasta do projeto podem consultar, mas não substituir arquivos.

A distribuição é self-contained para Windows x64, sem trimming e sem single-file no aplicativo principal, preservando as dependências nativas do LibVLC. O updater é um executável separado e temporário: espera o GuiaPlay encerrar, faz backup, aplica o staging, tenta rollback em falha e reinicia a aplicação. O log dele fica em `%LocalAppData%\GuiaSys\GuiaPlay\updater.log`.

## Integração com Windows e Explorer

A integração é sempre opt-in, por usuário e sem elevação. O Setup oferece duas tarefas inicialmente desmarcadas; as mesmas opções podem ser ativadas ou removidas depois em Configurações. O estado é detectado diretamente no Registry, sem duplicação no `settings.json`.

O GuiaPlay registra `GuiaPlay.Video` e `GuiaPlay.Audio`, `OpenWithProgids` somente para as 23 extensões reconhecidas pelo classificador, uma aplicação em `Software\Classes\Applications\GuiaPlay.exe`, capacidades em `Software\Clients\Media\GuiaPlay\Capabilities` e a referência correspondente em `Software\RegisteredApplications`. O menu de contexto usa verbos próprios em `Software\Classes\SystemFileAssociations\<extensão>\shell\GuiaPlay.Open`. Todas as chaves são relativas a `HKEY_CURRENT_USER`.

Nenhum valor padrão de extensão ou `UserChoice` é alterado. Para escolher o player padrão, use o botão que abre as Configurações oficiais do Windows. A desinstalação remove somente ProgIDs, valores, capacidades e verbos pertencentes ao GuiaPlay. Entradas de outros players são preservadas.

O comando registrado é `"GuiaPlay.exe" "%1"`. O arquivo passa pelo classificador antes de ser carregado; scripts e executáveis nunca são executados como mídia. A integração M08 usa um arquivo por invocação. Quando o GuiaPlay já está aberto, o mecanismo existente de Mutex + Named Pipe encaminha o caminho à mesma janela, traz a aplicação para frente e carrega sem autoplay.

### Como publicar uma nova versão

1. Atualize `VersionPrefix`, `VersionSuffix` e `ProductReleaseDate` somente em `Directory.Build.props` e crie as release notes correspondentes.
2. Execute `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-release.ps1` e corrija qualquer falha. O script valida formato, builds/testes Debug e Release, publish, LibVLC, updater, instalador, manifesto e checksums.
3. Revise `git status`, `git diff` e confirme que `artifacts/`, dados pessoais, logs e segredos não serão versionados.
4. Faça commit e `git push origin main`; confirme `main` sincronizada e limpa.
5. Execute `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish-release.ps1`. Ele recusa branch/origin/árvore incorretos, tags ou releases existentes e checksums divergentes; então cria a tag anotada, envia a tag e publica a prerelease com os quatro assets.
6. Consulte a Release pela API/`gh`, baixe os assets em uma pasta temporária e valide novamente os hashes.

## Limites conhecidos do protótipo

- A distribuição usa quadros BGRA/RV32 em memória de CPU. Há uma única decodificação e um único relógio LibVLC, mas a cópia CPU e a composição de cada monitor têm custo; não se declara aceleração por GPU.
- TVs e projetores podem adicionar atrasos próprios. Mesma fonte/relógio não significa sincronismo físico perfeito dos painéis.
- A identidade persistente de tela usa o caminho retornado pelo Windows via `QueryDisplayConfig`. Trocar porta, adaptador ou driver pode mudar esse caminho; correspondências ausentes ou ambíguas exigem escolha humana.
- A lista explícita de áudio contém somente pares módulo/dispositivo que o LibVLC informou aceitar. Lista vazia não prova ausência de áudio no Windows; **Padrão do Windows** continua disponível.
- A aplicação pode detectar uma perda de dispositivo depois de um redirecionamento transitório feito pelo backend/Windows. Ela pausa e não faz fallback nem retoma deliberadamente, mas ausência absoluta de transiente requer validação física.
- A classificação inicial de mídia usa extensões comuns; a decodificação efetiva continua sendo responsabilidade do LibVLC e depende do conteúdo/codecs do arquivo.
- O upgrade público de `0.7.0-prototipo` para `0.8.0-prototipo` usa o mesmo fluxo de GitHub Releases, manifesto e SHA-256 exercitado em diretórios temporários antes da publicação.
- O menu de contexto pode ser apresentado pelo Windows 11 dentro de **Mostrar mais opções**, conforme a política do Explorer; o GuiaPlay não instala uma shell extension DLL.
- Não há assinatura Authenticode nesta etapa; integridade do pacote de atualização é protegida pelo manifesto e SHA-256 publicado.

## Argumento de linha de comando e instância única

Um arquivo compatível pode ser carregado sem reprodução automática:

```powershell
.\GuiaPlay.exe "D:\Midias\Abertura.mp4"
```

Se o GuiaPlay já estiver aberto, a nova execução encaminha o caminho por Named Pipe para a janela existente e termina. O M04 não registra associações de arquivos nem altera o player padrão do Windows.

O símbolo e o wordmark oficiais ficam em `src/GuiaPlay.App/Assets/Branding/`. Os ícones funcionais da interface são SVGs 24 Regular do projeto oficial [Microsoft Fluent UI System Icons](https://github.com/microsoft/fluentui-system-icons), usados sob licença MIT; a licença está em `src/GuiaPlay.App/Assets/Icons/LICENSE-Fluent-System-Icons.txt`.
