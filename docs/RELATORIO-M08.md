# Relatório M08 — Integração com Windows e Explorer

Versão: **0.8.0-prototipo**

Data da versão: **26/09/2026**

Canal: **Prototype**

## Entrega

O M08 registra opcionalmente o GuiaPlay como aplicativo de mídia compatível e adiciona um verbo de Explorer sem mudar o player padrão. Toda a integração é por usuário em `HKEY_CURRENT_USER`, não exige administrador e não cria serviço, watcher, processo residente ou shell extension DLL.

## Registro e capacidades

- aplicação: `HKCU\Software\Classes\Applications\GuiaPlay.exe`;
- ProgIDs próprios: `HKCU\Software\Classes\GuiaPlay.Video` e `GuiaPlay.Audio`;
- candidatos por extensão: `HKCU\Software\Classes\.<ext>\OpenWithProgids`;
- capacidades: `HKCU\Software\Clients\Media\GuiaPlay\Capabilities`;
- aplicação registrada: valor `GuiaPlay` em `HKCU\Software\RegisteredApplications`;
- descoberta do executável: `HKCU\Software\Microsoft\Windows\CurrentVersion\App Paths\GuiaPlay.exe`;
- menu: `HKCU\Software\Classes\SystemFileAssociations\.<ext>\shell\GuiaPlay.Open`.

Vídeo usa `GuiaPlay.Video`; áudio usa `GuiaPlay.Audio`. `FriendlyTypeName`, `DefaultIcon` e `shell\open\command` usam o executável e ícone oficiais. O comando é sempre equivalente a `"C:\...\GuiaPlay.exe" "%1"`, sem interpolar o arquivo como comando arbitrário.

As 23 extensões vêm diretamente do `MediaTypeDetector`; não há segunda lista na lógica de aplicação. A extensão apenas autoriza uma tentativa de abertura. Compatibilidade e decodificação continuam sob responsabilidade do LibVLC.

## Opt-in e player padrão

As tarefas **Integrar GuiaPlay ao menu Abrir com do Windows** e **Adicionar Abrir com GuiaPlay ao menu de contexto** começam desmarcadas no Setup. A nova guia de Configurações detecta o estado real no Registry e permite registrar/remover cada parte posteriormente.

O GuiaPlay nunca grava `UserChoice`, hashes de associação ou o valor padrão de uma extensão. Um botão abre `ms-settings:defaultapps`; qualquer escolha de padrão continua sob controle do usuário e do Windows.

## Instância única e argumentos

Explorer chama o executável com um caminho entre aspas. O parser normaliza, verifica existência e rejeita tipos desconhecidos, incluindo `.bat`, `.cmd`, `.ps1` e `.exe`. Caminhos com espaços, acentos, parênteses e Unicode são tratados como dados. Caminhos UNC acessíveis usam a referência original; indisponibilidade resulta em aviso inline.

Mutex e Named Pipe do M04 permanecem como único mecanismo de instância. Uma segunda execução encaminha o caminho, encerra e a janela existente vem à frente para carregar sem autoplay. O M08 limita deliberadamente o shell a um arquivo por invocação (`%1`); suporte de lote não foi improvisado.

## Desinstalação e atualização

O desinstalador chama uma rotina silenciosa do próprio GuiaPlay antes de remover os binários. Ela apaga árvores próprias e somente os valores GuiaPlay em chaves compartilhadas, preservando outros players e sem tocar em `UserChoice`. Entradas criadas tanto pelo Setup quanto pela interface são removidas. O updater 0.7 → 0.8 apenas substitui binários na mesma pasta e não altera Registry, preservando integrações existentes.

## Arquitetura e testes

`WindowsIntegrationService` concentra geração, detecção, registro, remoção e idempotência. `IRegistryStore` impede Registry real em testes unitários; o fake em memória cobre vídeo, áudio, `OpenWithProgids`, capacidades, aplicação registrada, comandos, remoção, terceiros e idempotência. Um teste de integração separado mapeia todas as operações para `HKCU\Software\GuiaSys\GuiaPlay\Tests\<guid>` e apaga a subchave ao final.

Também há cobertura para caminhos especiais, scripts/executáveis rejeitados, UNC indisponível e encaminhamento de mídia Unicode à instância existente. Nenhum item do M09 ou M10 foi iniciado.

Resultados antes da publicação:

- `dotnet format --verify-no-changes`: aprovado;
- build Debug e Release: 0 erros e 0 warnings;
- testes Debug e Release: 147/147 aprovados em cada configuração;
- ZIP: `4478de1bd575ecb5cecab656bd598595267dd256dfda970eb2d88b158a77296f`;
- Setup: `53a7045bc6c79d21c19b8b9d2d68cdbcf8a98c5c406a4f3958eb5c46048bbb2e`;
- manifesto conferido contra o ZIP e 1.667 entradas do pacote validadas, incluindo `GuiaPlay.exe` e `GuiaPlay.Updater.exe`.

As consultas reais de atualização 0.7 → 0.8 e 0.8 → atual são executadas após a publicação da prerelease. A validação física do Explorer, USB, instalação e desinstalação continua no roteiro manual.
