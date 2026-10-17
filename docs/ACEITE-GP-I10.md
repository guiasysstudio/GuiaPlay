# GP-I10 — Protocolo de aceite da Interface 3.0 (Modelo 1)

**Projeto:** GuiaPlay / .NET 10 WPF / LibVLCSharp / WebView2.  
**Branch única de validação:** `feat/gp-interface-3-modelo1`.  
**Condição de entrada:** executar compilação e testes antes da validação manual.  
**Condição de saída:** nenhuma falha crítica, todos os itens essenciais marcados PASSOU; nenhum instalador, tag ou GitHub Release antes do aceite explícito.

## 1. Ambiente

Registrar ao executar: data/hora, SHA do Git, Windows (edição/build), SDK .NET, WebView2 Runtime, GPU/driver, resolução/DPI e quantidade/identidade de monitores, modo de projeção **Estender**. Nunca registrar licenças/segredos em logs.

### Script CLI de validação (PowerShell, VS Code integrado)

A partir da raiz clonada da branch de desenvolvimento:

```powershell
git fetch origin
git switch feat/gp-interface-3-modelo1
git pull --ff-only origin feat/gp-interface-3-modelo1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\gp-i10-validation.ps1 -BuildLocalInstallers
```

O script compila/testa Debug e Release, prepara **pastas de smoke** para `win-x64` e `win-x86`, verifica os recursos do player e, com `-BuildLocalInstallers`, **gera dois instaladores locais de TESTE** usando Inno Setup 6. Logs em `artifacts/gp-i10-validation/validation-transcript.txt`. Instaladores em `artifacts/gp-i10-local-installers/`, com SHA-256. **Não há upload, tag, merge, Release nem publicação.** Se algum comando falhar, interromper o gate.

**Correção GP-I11 — PowerShell:** em Windows PowerShell 5.1, arquivos UTF-8 sem BOM com acentos e aspas não ASCII podem ser interpretados como ANSI, produzindo `TerminatorExpectedAtEndOfString`. Os scripts GP-I10 foram reescritos em ASCII e tiveram sua análise sintática aprovada pelo próprio Windows PowerShell 5.1 no CI.

**Segurança do instalador de teste:** o instalador experimental usa AppId e diretório próprios para cada arquitetura, não substitui o GuiaPlay estável nem registra associações de arquivo. Antes de gerar o instalador, o script faz backup privado de `%LOCALAPPDATA%\GuiaSys\GuiaPlay` quando presente. A aplicação de teste, porém, ainda utiliza o mesmo perfil de dados e o mecanismo de instância única: feche completamente o GuiaPlay estável antes do teste e nunca execute as duas versões ao mesmo tempo. Inno Setup 6 (`ISCC.exe`) é obrigatório apenas para gerar o instalador.

**Importante:** **não** executar `scripts/build-release.ps1` nesta etapa, pois ele exclui `artifacts/` e gera artefatos identificados com a versão estável 1.0.3. Também **não** executar `scripts/publish-release.ps1`.

## 2. Cenários essenciais — operador / interface

| ID | Passo / caso | Aceite obrigatório |
|---|---|---|
| UI-01 | Abrir em 1920×1080, 100% | Header, playlists, central de mídia, Programa, áudio, transporte legíveis, nenhum item fora de acesso |
| UI-02 | Abrir em 1366×768, 100% | Barra de transporte, controles de telas, sidebar rolável e lista acessíveis, sem sobreposição |
| UI-03 | Escala Windows 125% e 150% | Texto, botões e previews sem corte; áreas de clique coerentes |
| UI-04 | Tab / Shift+Tab / Enter | Foco visível, botões e combos acionáveis; sem armadilha de teclado |
| UI-05 | Janela sem licença / licença expirada | Workspace e transporte bloqueados, configurações/licenciamento acessíveis; nenhuma tela pública ativa |
| UI-06 | Versão, Status, Atualização, Sobre, Monitores, Configurações, Atalhos | Cada botão executa a função indicada, sem botões cenográficos |
| UI-07 | Acesso por mouse em todo modal | Cancelar/fechar preserva alterações pendentes; Esc funciona conforme contexto |

## 3. Playlists, pastas e tabela

| ID | Passo / caso | Aceite obrigatório |
|---|---|---|
| PL-01 | Criar playlist e grupos | Aparece na biblioteca de trabalho e pode salvar/reabrir |
| PL-02 | Salvar e abrir duas playlists nomeadas | Lista correta sem misturar dados; alterações não salvas geram confirmação |
| PL-03 | Renomear e remover grupo | Não modifica mídia física; persiste |
| PL-04 | Excluir preset salvo | Confirma exclusão; não elimina áudio/vídeo físico |
| PL-05 | Importar 1, 50 e 500 arquivos | Responsividade e ordem preservadas |
| PL-06 | Importar pasta com 4 níveis de subpastas | Hierarquia virtual estável e item acessível |
| PL-07 | Reimportar a mesma pasta | Duplicatas ignoradas sem perder itens |
| PL-08 | Arrastar pasta do Explorer | Mesmo resultado do menu; sem copiar/mover arquivos |
| PL-09 | Pasta inacessível, pasta vazia e cancelamento | Erros/avisos legíveis; sem criação fantasma nem playlist corrompida |
| PL-10 | Busca por nome / tipo e mover item | Busca só filtra visualização; não altera ordem persistida |
| PL-11 | Abrir mídia ausente / arquivo movido | Mensagem real; playlist permanece recuperável |
| PL-12 | Importar áudio puro e vídeo, renomear preset, reiniciar | Integridade preservada |

## 4. Reprodução LibVLC

| ID | Passo / caso | Aceite obrigatório |
|---|---|---|
| VLC-01 | Selecionar operador e 1 monitor público real | Operador jamais se torna saída pública; Programa reflete o quadro transmitido |
| VLC-02 | Selecionar operador e 2 saídas públicas | Uma sessão de vídeo com 2 janelas; sem decodificador extra por monitor |
| VLC-03 | Play, Pause, Stop, Play novamente | Estado e vídeo corretos; não exibir janela órfã |
| VLC-04 | Scrub por clique, arraste nas duas timelines e roda do mouse | Posição real e indicadores sincronizados sem saltos indevidos |
| VLC-05 | ±5s em vídeo curto e no fim da duração | Clamp 0…duração, sem exceção |
| VLC-06 | Arquivo não seekable / duração desconhecida | Seek desabilitado e valores honestos |
| VLC-07 | Anterior e Próxima durante pausa/reprodução | Confirmar substituição; nova mídia carregada sem autoplay |
| VLC-08 | Modo Manual e fim natural | Não reproduzir próximo item automaticamente |
| VLC-09 | Modo Sequencial e fim natural | Avanço somente para próxima mídia disponível |
| VLC-10 | Repetir Atual, Repetir Lista, Aleatório | Política correta e sem loops indevidos |
| VLC-11 | Alterar playlist enquanto roda | Não iniciar fila antiga após fim |
| VLC-12 | Perda de projetor / monitor do operador | Pausar e fechar saída conforme proteção, sem mover vídeo para operador |
| VLC-13 | Desconectar saída de áudio explícita | Sem fallback silencioso para dispositivo diferente |
| VLC-14 | Volume, Mudo, painel direito, configurações de áudio | Controles sincronizados |
| VLC-15 | Tela cheia do operador, Esc/F11, revogar licença | Mostra mesma fonte local e fecha com segurança |
| VLC-16 | Vídeo de 60+ minutos e pausa prolongada | Sem crescimento anormal de memória, sem atraso significativo |

## 5. YouTube — verificação isolada e **não presumir equivalência**

| ID | Passo / caso | Aceite obrigatório |
|---|---|---|
| YT-01 | Adicionar watch, youtu.be e Shorts públicos | Um ID canônico, sem escrita de stream local |
| YT-02 | Abrir com 1 tela pública selecionada | IFrame oficial visível; nenhuma janela LibVLC pública concorrente |
| YT-03 | Abrir com nenhuma ou 2 telas públicas | Bloqueio explícito; não exibir conteúdo na tela errada |
| YT-04 | Play/Pause/Stop/Seek/±5s/Volume/Mudo | API oficial responde; tempo/estado refletem eventos reais |
| YT-05 | Vídeo não incorporável | Erro 101/150 exibido, sem contorno |
| YT-06 | Privado/removido | Erro 100 ou equivalente; não iniciar outro vídeo automaticamente |
| YT-07 | Sem internet e retorno da internet | Timeout/erro legível; nenhuma janela fantasma |
| YT-08 | Código de erro 153 de identificação/Referer | Gate falha e integração NÃO liberada até corrigir origem autorizada |
| YT-09 | Bloqueio de autoplay e consentimento do player | Não contornar por áudio oculto ou stream extraction |
| YT-10 | Comportamento em DPI 100/125/150 e atribuição/ads | IFrame sempre visível, dimensões mínimas 200×200 px reais, sem overlays |
| YT-11 | Apagar playlist, usar presets v1/v2 | Dados antigos recuperáveis, backup legível, URL preservada |
| YT-12 | Trocar YouTube por vídeo local e vice-versa | No máximo um backend ativo; sem áudio duplo |
| YT-13 | Desconectar saída, operador, revogar licença | Player público fechado sem manter áudio em background |
| YT-14 | WebView2 Runtime ausente | Mensagem de erro real sem travar a interface |

**Não é critério de aceite:** prévia de quadros ao vivo para YouTube no operador, múltiplos projetores YouTube sincronizados ou áudio dirigido pelo LibVLC. A interface deve ser explícita sobre a ausência dessas capacidades.

## 6. Teclado

Verificar todos os 17 atalhos com a janela principal ativa. Repetir com foco em TextBox, ComboBox, árvore, tabela, barra de progresso e botão. **Espaço em botão/check box** deve conservar comportamento do Windows. **AltGr, Windows e teclas seguradas** não podem executar comandos extras. F1 abre ajuda sem bloquear digitação. F11 só mostra vídeo local do operador.

## 7. Segurança, regressão e entrega

- Confirmar licenciamento GSL-v1, verificação de atualizações, ícones, branding e dispositivos anteriores sem mudança indevida.
- Não coletar URLs completas com tokens na telemetria, nem CPF/chaves de licença.
- Rever conformidade com IFrame Player API, branding, ads, atribuição, consentimento, privacidade, Referer e direitos de exibição pública.
- Não fazer push em `main`, merge, tag, release, upload de instalador ou deploy até autorização do responsável.
- Se qualquer cenário essencial falhar, abrir uma correção GP-I11+ com evidências; repetir CI e cenários de regressão.

## 8. Resultado

**PENDENTE:** preencher quando o operador retornar com a saída do script e resultados físicos, sem declarar teste aprovado antes de executá-lo.

| Verificação | Resultado | Observação |
|---|---|---|
| Build/teste Windows em CI | A apurar | Workflow Windows na branch |
| Build/teste Debug e Release no PC | Pendente | Script gp-i10-validation.ps1 |
| Publicação smoke win-x86/win-x64 | Pendente | Somente diretórios, sem instalador |
| Media local / 2 monitores / áudio | Pendente | Teste físico |
| YouTube WebView2 / Referer 153 | Pendente | Teste físico e conformidade |
| Aprovação comercial | Não concedida | Solicitar depois do gate |
