# GuiaPlay — Interface 3.0 / Modelo 1 (plano oficial de execução)

Data-base: 2026-10-16. Referência de produto: GuiaPlay 1.0.3 Stable, branch `main`. Branch de trabalho: `feat/gp-interface-3-modelo1`.

## Objetivo

Reconstruir o espaço de trabalho do operador segundo o **Modelo 1 aprovado**: cabeçalho/navegação; status de licença; coluna de playlists e pastas; prévia do operador e monitor do programa; seleção de saídas e áudio; barra de ações; tabela de mídias; transporte inferior.

**Regra inegociável:** nenhum controle decorativo na interface entregue. Cada botão, campo, indicador e atalho visível deve ter comportamento real, estado habilitado/desabilitado coerente, descrição acessível e teste adequado. Controles que dependam de funcionalidades futuras não serão exibidos como se já estivessem funcionais. A imagem é referência visual; não é especificação pixel-perfect de dimensões.

## Proteção do produto atual

- Não modificar `main`, não criar tag/Release, não gerar instalador/ZIP de distribuição e não publicar versão durante esta fase.
- Trabalhar por commits pequenos exclusivamente na branch de desenvolvimento; copiar mudanças ao computador para build e teste pelo VS Code.
- Preservar LibVLC, única sessão/relógio, janelas públicas, roteamento de áudio, bloqueios de operador/saídas, pause em perda de tela/dispositivo, lifecycle e saída limpa.
- Preservar GuiaSys Licensing GSL-v1 (trial, status, licença, cache offline seguro), updater Stable (manifesto, RID e rollback), configuração/presets existentes e associação opcional ao Windows.
- Funcionalidade nova nunca deve alterar arquivos de mídia originais ou apagar dados/presets de usuários.
- Preparar a validação futura de **Windows x64 e x86** e resoluções pequenas (mínimo 1366×768 para teste físico), escalas de DPI 100%, 125%, 150%; alto contraste e tabulação/navegação por teclado.
- Sem atualização de schema sem migração bidirecionalmente segura no sentido de preservação de dados; não assumir que versões antigas conseguem ler dados novos.
- Não afirmar teste físico concluído sem execução no Windows real; builds/testes automatizados separados do smoke manual.

## Inventário inicial de código (auditado no GitHub)

- `src/GuiaPlay.App/MainWindow.xaml`: workspace atual em dois painéis (Prévia e TreeView Playlist), com controles inferiores, status de licença/atualização e seleção de saídas.
- `src/GuiaPlay.App/MainWindow.xaml.cs`: lógica atual dos comandos, arrastar e soltar grupos/itens, probe, controles de seek, abertura e licenciamento; preservar funcionalidade na migração visual.
- `src/GuiaPlay.Core/Playlist.cs`: `PlaylistCatalog`, persistência versionada e ordenação de grupos/itens.
- `src/GuiaPlay.App/Models/PlaylistTreeNodes.cs`: apresentação de grupos e itens com disponibilidade.
- `src/GuiaPlay.App/Playback/LibVlcPlaybackEngine.cs`: fluxo local e frames compartilhados.
- `src/GuiaPlay.Core/LicensingManager.cs`: status real e gating de acesso.
- `tests/GuiaPlay.Core.Tests` e `tests/GuiaPlay.App.Tests`: testes existentes a preservar e ampliar.
- Já existem drag-and-drop e seek na barra operacional; não reimplementar à toa. Falta o layout tabular, bibliotecas laterais, metadados e controles duplicados sincronizados.

## Plano de implementação (11 partes)

| Parte | Nome | Entregável / gate |
|---|---|---|
| **GP-I00** | Auditoria e baseline | Branch isolada, contrato visual e funcional, linha de base preservada. |
| **GP-I01** | Estrutura visual | **Implementação registrada**: cabeçalho funcional, versão, licença, navegação e shell de três colunas; validação local conjunta após GP-I10. |
| **GP-I02** | Biblioteca lateral | **Implementação registrada:** presets reais na biblioteca, contadores/estado ativo, Nova/Salvar/Abrir/Excluir, confirmação de alterações, e árvore de grupos virtuais; importação física de pastas permanece no GP-I04. Testes de execução reservados ao GP-I10. |
| **GP-I03** | Grade profissional de mídias | **Implementação registrada:** tabela de reprodução com miniaturas locais disponíveis, metadados verificáveis, pesquisa não destrutiva, menu funcional e ordem por arraste e setas; limites e validação Windows documentados em `RELATORIO-GP-I03.md`. |
| **GP-I04** | Importação de diretórios | **Implementada no código:** Adicionar arquivo(s)/pasta, varredura de subpastas, grupos nomeados pelo caminho relativo, importação via arraste, duplicatas, cancelamento, importação parcial confirmada, persistência antes de aplicar. Validação física pendente do GP-I10. |
| **GP-I05** | Prévia, programa, telas e áudio | **Implementada no código:** prévias de operador e Programa a partir do mesmo frame, estados provenientes das janelas públicas reais, monitor do operador excluído, rotas de áudio e volume sincronizados; troca de dispositivo pelo painel de áudio existente. Verificação física pendente do GP-I10. |
| **GP-I06** | Controles de reprodução | Transporte inferior, duas timelines sincronizadas e interativas, volume, anterior/próximo, ±5 s; modos manual/sequencial explícitos, evitar autoplay acidental. |
| **GP-I07** | Atalhos do operador | Comandos locais de teclado, ajuda e conflitos/foco; não capturar teclas de digitação; teste de acessibilidade. |
| **GP-I08** | YouTube — viabilidade técnica | Protótipo isolado com player oficial/termos aplicáveis: URL, play/pause/seek, preview/saídas e sincronismo. Gate GO/NO-GO antes de prometer equivalência total. |
| **GP-I09** | YouTube — integração funcional | `Adicionar > Link do YouTube`, tipo e persistência, erro de rede/restrições de incorporação, controles na mesma região, comportamento de áudio/saídas e testes. |
| **GP-I10** | Qualidade e aceite local | Refinar fidelidade ao Modelo 1, testes automatizados, build Debug/Release, smoke no VS Code e matriz x86/x64/DPI/monitores, sem instalador. |

O **controle remoto Android/LAN** é projeto futuro separado, após conclusão e validação de todos os itens acima.

## Contrato dos componentes visuais

**Barra superior:** logo/versão; Playlist (seleção/presets), Mídia (gerenciamento/importações), Configurações (configurações existentes), Monitores (configuração/identificação), Sobre (página Sobre existente), atualização existente, sinal real de licença: checking / trial (+ dias conforme backend) / ativa / bloqueada.

**Coluna esquerda:** presets/listas salvos, contador real; área Pastas/Recentes/Favoritos apenas quando implementadas em fonte real de dados e persistência adequada, nunca como elementos cenográficos.

**Centro:** prévia operador, estado Programa (saídas reais), sem emitir vídeo ao monitor operador como saída; frame e tempo correspondem ao mesmo player. Timeline da prévia e timeline inferior controlam a mesma posição do motor, respeitando mídia não seekable.

**Coluna direita:** saídas públicas com seleção real e monitor do operador excluído; dispositivo de áudio real e volume efetivo.

**Grade:** índice é a ordem operacional de reprodução, e ordenação visual de metadados não deve alterar essa ordem silenciosamente. Miniaturas e metadados são coletados com cancelamento/cotas de recursos e fallback para áudio, arquivo ausente ou mídia não decodificável. Datas são provenientes de metadados locais verificados, não valores inventados.

**Ações:** Adicionar é menu com `Arquivo(s)`, `Pasta` e `Link do YouTube` (este último só fica disponível quando GP-I09 for aprovado); Nova Playlist, Salvar, Remover, subir/descer, filtro/busca. Remover referência não exclui original. 
**Transporte:** anterior, retroceder 5 s, play/pausa, avançar 5 s, próximo, parar, mute/volume, progresso, duração/decorrido. Implementar também todos os botões retratados no Modelo 1 que integrarem a barra final, incluindo aleatório, repetição e tela cheia, cada qual com comando efetivo, indicadores de estado e testes; manter reprodução manual como padrão e evitar avanço automático não solicitado.

## Particularidades do YouTube (bloqueios técnicos)

- Não tratar página do YouTube como arquivo MP4 local nem capturar/extrair fluxos, baixar conteúdos ou burlar restrições do serviço.
- Investigar incorporação pelo player oficial (API de player embutido/WebView2) e restrições de comercialização, exibição e reprodução em vários monitores.
- A interface dos controles será unificada, mas os recursos só serão habilitados quando o backend do YouTube realmente suportá-los (ex.: seek e duração podem variar).
- Se a reprodução oficial em saídas públicas simultâneas não puder cumprir os requisitos sem violar regras, registrar bloqueio e solicitar decisão de produto — não implementar um atalho não conforme.

## Regra de execução revisada (decisão de 16/10/2026)

- Implementar as **onze partes GP-I00 a GP-I10 primeiro**, em sequência, na branch de desenvolvimento; o usuário executará os testes integrados no VS Code **apenas depois de GP-I10**.
- Manter revisão estática por alteração e fazer commits pequenos para evitar corromper o projeto. Revisão estática não significa afirmar que o software foi compilado ou que testes passaram.
- Não enviar comandos de teste local ao usuário por etapa, salvo solicitação específica. Entregar os comandos completos de restore, build, test, run e roteiro de smoke após todas as partes.
- Se a etapa de teste final revelar problemas, criar etapas **GP-I11+** de correção antes de pensar em instalador ou Release.
- A validação do próprio YouTube (restrições de incorporação, APIs e direitos) permanece um gate de viabilidade **técnica** durante GP-I08, pois não é correto implementar reprodução irregular até o teste final.

## Fluxo do VS Code (não gera instalador)

```powershell
git fetch origin
git switch feat/gp-interface-3-modelo1
git pull --ff-only origin feat/gp-interface-3-modelo1

dotnet restore .\GuiaPlay.slnx
dotnet build .\GuiaPlay.slnx -c Debug
dotnet test .\GuiaPlay.slnx -c Debug
dotnet run --project .\src\GuiaPlay.App\GuiaPlay.App.csproj -c Debug
```

**Observação:** rodar `dotnet run` nesta fase não executa instalador nem substitui o executável instalado. Evitar iniciar update enquanto realiza smoke manual; usar conteúdo de teste. Não rodar `scripts/build-release.ps1` nem `scripts/publish-release.ps1` nesta etapa.
