# RELATÓRIO — GP-I10 (Interface 3.0 Modelo 1)

Data: 17/10/2026.  
Branch: `feat/gp-interface-3-modelo1`.  
Gate: **GO para smoke físico controlado no Windows**; **NÃO é GO para Release, instalação comercial ou merge em main**.  
CI testado no commit: `7ff9ae6563b8e477650e333cce182e5338428756`.  
Execução: https://github.com/guiasysstudio/GuiaPlay/actions/runs/38997672598

## Trabalho executado

- Habilitação do workflow Windows para a branch de Interface 3.0, com controle de concorrência para cancelar builds obsoletos da mesma branch.
- Adição do script `tools/gp-i10-validation.ps1` para validar Debug/Release e preparar diretórios de smoke x64/x86 no PC do responsável **sem criar instaladores, tags, releases nem deploy**.
- Criação da matriz de aceite `docs/ACEITE-GP-I10.md` abrangendo layout, acessibilidade, monitores, playlists, mídia local, YouTube, licenciamento, atalhos, falha de rede e validação física.
- Correções obtidas por compilação CI real: evento de estado do YouTube renomeado para não ocultar `Window.StateChanged`; `using System.IO` em `MediaGridMetadataService`.
- Atualização de testes antigos de branding M10/M10.3 para os componentes **reais** da Interface Modelo 1 (layout de três colunas, previews, tabela e comandos de playlists). As verificações não foram suprimidas.
- Redução de flutuações causadas por teste paralelo com operações nativas: limite de concorrência do conjunto Core, tolerância de agendamento para testes de sondagem de arquivo local e prazo limitado maior para conexões de segundo processo.
- Correção do script de x86 para forçar **`PlatformTarget=x86`** além de `-r win-x86`, já que `Directory.Build.props` utiliza x64 por padrão.

## Resultado obtido no CI do GitHub

| Verificação | Resultado |
|---|---|
| .NET 10 Windows Restore | **PASSOU** |
| Build Debug (win-x64) | **PASSOU — 0 erros / 0 warnings** |
| Testes Debug App | **33/33 PASSOU** |
| Testes Debug Core | **394/394 PASSOU** |
| Build Release (win-x64) | **PASSOU — 0 erros / 0 warnings** |
| Testes Release App | **33/33 PASSOU** |
| Testes Release Core | **394/394 PASSOU** |
| Restore + Publish smoke win-x64 | **PASSOU** |
| Restore + Publish smoke win-x86 | **PASSOU** |
| Verificação do `GuiaPlay.exe` em ambas as arquiteturas | **PASSOU** |
| Verificação de `Assets/YouTube/player.html` em ambas | **PASSOU** |

**Total:** 427 casos distintos, executados em Debug e Release = **854/854 aprovações** (sem contar restaurações, compilação e publicações smoke).  
Os diretórios smoke foram construídos no runner e **não publicados como instaladores nem Releases**.

## O que NÃO foi validado — bloqueios antes da publicação

O CI não dispõe dos monitores, projetores, GPU, saídas de áudio e WebView2 interativo do PC do operador. Por isso **não comprova**:

1. Funcionamento visual e responsivo real em 1366×768, 1920×1080 e DPI 100/125/150%.
2. Reprodução física do LibVLC em tela estendida; prévia e Programa; latência, perda de monitor e áudio.
3. Controles por teclado/mouse dentro de modais; drag-drop de pastas do Explorer.
4. Execução do player WebView2/YouTube, `Referer`, erro 153, ads/atribuição, disponibilidade de vídeos e consentimento.
5. Estabilidade prolongada, dispositivos x86 reais, Windows 10 do operador, instalação/desinstalação e atualização ponta a ponta.
6. Autorização comercial para usar player oficial em apresentações e conformidade dos direitos de conteúdo.

**Regra de liberação:** qualquer falha nesses cenários gera GP-I11+; não gerar instalador nem tag/release até aceite explícito.

## Como começar o smoke no PC

Abra no VS Code a pasta raiz do clone `GuiaPlay`. No terminal PowerShell:

```powershell
git fetch origin
git switch feat/gp-interface-3-modelo1
git pull --ff-only origin feat/gp-interface-3-modelo1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\gp-i10-validation.ps1
```

Após sucesso, executar o GuiaPlay x64 via pasta de smoke (não instalador), com **monitor do operador e projetor configurados como telas estendidas**, respeitando `docs/ACEITE-GP-I10.md`. Retornar logs e ocorrências para correção. Não enviar chaves/segredos nem conteúdo pessoal dos arquivos.

## Proteção de produção

- `main` intacta.
- Sem tag, instalador, release, merge ou alteração do licenciamento em produção.
- GP-I00 até GP-I10 terminadas **em código e validação automatizada de integração**; a aceitação física é a próxima ação humana.
