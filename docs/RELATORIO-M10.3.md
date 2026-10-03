# Relatório técnico M10.3 — GuiaPlay 0.10.3-prototipo

Data da execução: **03/10/2026**

Data de produto configurada: **02/10/2026**

Canal: **Prototype**

Estado: **candidata de engenharia gerada; não publicada**

Este relatório separa evidência automatizada de validação física. A tag `v0.10.3-prototipo` e a GitHub Release não foram criadas porque o gate humano ainda contém itens obrigatórios sem evidência.

## Versão e escopo

- `VersionPrefix`: `0.10.3`
- `VersionSuffix`: `prototipo`
- versão pública: `0.10.3-prototipo`
- runtime: `.NET 10`, WPF, self-contained
- motor: `LibVLCSharp 3.10.1` e `VideoLAN.LibVLC.Windows 3.0.24`
- RIDs produzidos: `win-x64` e `win-x86`
- fora do escopo: Android remoto, streaming, YouTube, nuvem, contas, login, licenciamento, plugins, letras e captura

## Testes

| Configuração | Core | App | Total | Falhas | Ignorados |
|---|---:|---:|---:|---:|---:|
| Debug | 272 | 31 | 303 | 0 | 0 |
| Release | 272 | 31 | 303 | 0 | 0 |

- testes únicos: **303**;
- execuções na matriz Debug + Release: **606**;
- nenhuma exclusão ou desativação de teste para obter resultado verde;
- cobertura nova inclui SemVer/canais/RIDs, manifestos legado e multiarch, SHA inválido, updater/rollback, pacote incompleto, falha durante backup, ZIP, probes limitados, single instance, configurações, playlist parcialmente inválida, presets, 1.000 referências, ciclos de playback e concorrência/teardown do framebuffer.

## Build e validações estáticas

| Gate | Resultado |
|---|---|
| `dotnet restore GuiaPlay.slnx` | aprovado |
| `dotnet format GuiaPlay.slnx --verify-no-changes --no-restore` | aprovado |
| build Debug | 0 avisos, 0 erros |
| build Release | 0 avisos, 0 erros |
| sintaxe dos quatro scripts PowerShell | aprovada pelo parser |
| `node --check site/assets/js/app.js` | aprovado |
| links/assets locais do site | todos resolvidos |
| Inno Setup | x64 e x86 compilados com Inno Setup 6.7.3 |
| arquitetura PE | EXE, updater, `libvlc.dll` e `libvlccore.dll` conferidos como x64/x86 conforme o RID |

O pipeline final foi executado por `scripts/build-release.ps1`, do restore à criação dos checksums e manifesto.

## Auditoria e correções

| Área / achado | Estado | Evidência da correção |
|---|---|---|
| SemVer comparava identificadores RC como texto | corrigido | comparação numérica cobre `rc2 < rc10` e aceita `rc.1`/`rc.2` |
| canais presos a Prototype/prerelease | corrigido | políticas Prototype, ReleaseCandidate e Stable em runtime e scripts |
| manifesto apenas x64 | corrigido | `package` legado permanece alias de x64 e `packages` seleciona o RID exato |
| downgrade ou troca x64/x86 | corrigido | validação no manifesto, app, staging, updater, aplicador e instalador |
| callback de vídeo podia receber plano nulo durante saturação/shutdown | corrigido | buffer de descarte nativo válido é preservado até quiescência |
| cópia de `FrameLease` podia liberar um slot reutilizado (ABA) | corrigido | estado de liberação one-shot compartilhado com `Interlocked.Exchange` |
| chamadas ao handle LibVLC competiam com `MediaPlayer.Dispose` | corrigido | `_nativeGate` serializa leitura, controle, áudio, stop e dispose |
| callback tardio de sessão antiga podia alcançar a nova sessão | corrigido | identidade da sessão é validada sob `_callbackGate`; exceções de consumidores são contidas |
| `DisposeAsync` concorrente retornava antes do teardown em curso | corrigido | uma única task de descarte é compartilhada por todos os chamadores |
| exceção podia escapar por reverse P/Invoke | corrigido | callbacks de formato/display/cleanup e logging são protegidos; formato inválido retorna zero |
| writers abandonados sobreviviam a limpezas de formato | corrigido | cleanup quiescente aposenta e libera writers sem esperar leases gerenciados |
| format callback declarava um buffer embora o ring tivesse três | corrigido | retorna `PictureBufferCount = 3` |
| sessão podia vazar se equalizador falhasse durante criação | corrigido | sessão parcialmente criada é parada e descartada no `catch` |
| teardown podia esperar indefinidamente por Reading/Writing | corrigido | aposentadoria não bloqueante e liberação diferida após callbacks pararem |
| probes de arquivo/rede bloqueavam Dispatcher e podiam crescer sem limite | corrigido | executor dedicado com 1–4 workers, fila 64, timeout, cancelamento e ordem preservada |
| áudio salvo indisponível impedia salvar aparência/equalizador/update | corrigido | preferência é preservada; alterações seguras continuam permitidas sem fallback silencioso |
| operador desconectado gerava instrução contraditória | corrigido | pausa causada por perda do operador libera somente a recuperação segura em Configurações > Telas |
| backup temporário acumulava indefinidamente | corrigido | workspaces marcados, retenção limitada e exclusão apenas de diretórios reconhecidos |
| falha durante a criação do backup podia limpar a instalação | corrigido | nenhum arquivo do destino é tocado antes do backup completo; regressão automatizada adicionada |
| rollback limpava o destino antes de restaurar | corrigido | restauração agora sobrescreve a partir do backup e só então remove arquivos novos obsoletos |
| pacote incompleto podia substituir uma instalação válida | corrigido | EXE, updater, LibVLC, core e plugins do RID são obrigatórios antes da primeira mutação |
| `install.json` permanecia na versão anterior | corrigido | versão e RID são gravados atomicamente somente após aplicação bem-sucedida |
| argumentos do Explorer não passavam pelo parser | corrigido | startup e encaminhamento usam probe assíncrono limitado; não há autoplay |
| disponibilidade de playlist era assumida como verdadeira | corrigido | estado real distingue disponível, ausente, timeout, inválido e falha de decode |
| drop inválido criava grupo fantasma | corrigido | grupo fallback nasce somente após ao menos uma mídia aceita |
| item inválido podia eliminar irmãos/grupos válidos | corrigido | codec tolerante preserva entradas válidas e emite warning |
| playlist não possuía presets reutilizáveis | corrigido | store, workspace, seletor e fluxos Save/Load/Overwrite/Delete/Dirty/Recovery implementados |
| mudança de topologia podia apagar/recriar saídas válidas | corrigido | reconciliação usa identidade persistente, reposiciona válidas e fecha somente desconectadas |
| falha de `Process.Start` alcançava o Dispatcher | corrigido | launcher seguro retorna erro; update manager converte falha em estado controlado |
| updater podia reiniciar caminho arbitrário | corrigido | reinício restrito ao `GuiaPlay.exe` existente dentro do destino gerenciado |
| Stop podia sinalizar estado falso após falha do motor | corrigido | coordenador entra em erro explícito e mantém recuperação possível |
| migração de single instance podia abrir duas versões | corrigido | endpoints atual e legado são adquiridos/escutados em conjunto; solicitações são serializadas |
| falha de escrita de settings podia substituir estado válido | corrigido | persistência atômica e retorno explícito sem promover o candidato em memória |
| ZIP permitia traversal ou consumo sem limite | corrigido | path containment, limite de 25.000 entradas e 4 GiB descompactados, com contagem durante extração |
| publicação não validava novamente os bytes remotos | corrigido | `publish-release.ps1` baixa todos os assets e compara SHA-256 local/remoto |

Não há crash, deadlock, perda de playlist/configuração, pacote cross-arch ou instalador inválido conhecido na cobertura automatizada atual. Isso não substitui o gate físico.

## Playlist presets

- pasta: `%LocalAppData%\GuiaSys\GuiaPlay\playlist-presets\`;
- schema: `1`;
- arquivo: `<guid>.json`, sem usar o nome visível no filesystem;
- Save: snapshot completo e ordenado, escrita temporária + flush + rename;
- Load: substitui a playlist de trabalho, persiste `playlist.json`, reprova disponibilidade e não inicia playback;
- Overwrite: exige confirmação, preserva ID/`createdAt` e atualiza `updatedAt`;
- Delete: remove apenas o JSON do preset;
- Dirty: cobre criação, renomeação, exclusão, movimentação e reordenação de grupos/itens;
- Recovery: preset corrompido é isolado; itens inválidos são ignorados individualmente; arquivo ausente continua referenciado;
- escala automatizada: snapshot com **1.000 referências** salvo/carregado na ordem exata.

## UI

| Medida | Antes | M10.3 |
|---|---|---|
| Prévia / Playlist | `3* / 2*` = 60% / 40% | `54* / 46*` = 54% / 46% |
| altura do painel inferior | `Auto`; botões 34 DIP, padding externo vertical 16 DIP e gaps de 6 DIP | `Auto`; botões 30 DIP, padding externo vertical 12 DIP e gaps de 4 DIP |

O painel não possui altura fixa; portanto não existe um único valor de altura independente de fonte, tema e DPI. A compactação devolve aproximadamente 26–30 DIPs verticais no layout nominal, sem fabricar uma medição física para escalas ainda não testadas. A toolbar da playlist ganhou Save/Load e mantém comandos com alvo mínimo de 30 DIP.

## Windows e arquiteturas

Base mínima planejada: **Windows 10 Enterprise LTSC 2019, versão 1809, build 17763**, enquanto suportada pelo Windows e pelo .NET 10. Windows 11 é elegível somente em versões/edições ainda suportadas. Windows 10 Home/Pro 22H2, Windows 7, 8 e 8.1 não integram a matriz oficial desta linha.

Ambiente automatizado desta execução:

- Windows 11 Pro 25H2, build `26200.9457`, host x64;
- SDK .NET `10.0.300`; runtime `10.0.8`;
- publish self-contained x64 e x86 concluído;
- bootstrap dos dois EXEs e hand-off de single instance concluídos com código `0`;
- a janela principal nova não pôde ser isolada porque uma instância instalada `0.10.2-prototipo` já estava aberta; ela foi preservada e não foi encerrada pelo teste;
- smoke de janela, playback e encerramento da nova build permanece pendente em sessão sem outra instância.

`win-x64` é a distribuição recomendada. `win-x86` contém runtime e LibVLC x86 reais e pode rodar via WOW64 em Windows x64; limites de endereço e comportamento em Windows 10 32-bit exigem máquina física.

Fontes técnicas primárias estão consolidadas em [COMPATIBILIDADE-WINDOWS.md](COMPATIBILIDADE-WINDOWS.md).

## Hardware

- CPU: nenhuma marca, geração ou extensão além das exigidas por Windows/.NET/LibVLC é selecionada pelo código; Intel e AMD precisam de gate real;
- GPU: não há requisito de GPU dedicada nem alegação de decode/render integralmente acelerado; WPF pode cair para software;
- RAM: depende do SO, mídia, resolução e buffers; x86 possui espaço de endereço menor e requer ensaio próprio;
- áudio/monitores: dependem dos drivers e endpoints expostos pelo Windows; HDMI, USB, onboard, Bluetooth, 1/2/3 monitores e DPIs distintos permanecem físicos.

## Branding

Fontes oficiais obrigatórias:

- `src/GuiaPlay.App/Assets/Branding/Source/GuiaPlay-Logo.svg`;
- `src/GuiaPlay.App/Assets/Branding/Source/GuiaPlay-Wordmark.svg`.

`scripts/generate-branding-assets.ps1` gera ícones PNG/ICO, wordmarks e imagens BMP/PNG do instalador. O build falha se fontes ou derivados essenciais estiverem ausentes/vazios. App e Setup usam os derivados oficiais; a inspeção visual final em DPI 100/125/150 continua no gate humano.

## Update

- comparação numérica de versões e RCs compactos/pontuados;
- Prototype aceita Prototype posterior, RC e Stable;
- ReleaseCandidate aceita RC posterior e Stable, nunca Prototype;
- Stable aceita somente Stable;
- versão igual ou inferior nunca é aplicada;
- manifesto legado x64 continua válido;
- manifesto novo seleciona `win-x64`/`win-x86` estritamente;
- SHA-256, versão, RID, payload e `install.json` são validados antes de alterar arquivos;
- download cancelado remove parcial; extração usa staging seguro;
- backup é limitado por retenção e rollback preserva o marcador anterior até o sucesso;
- teste real publicado `0.10.2 → 0.10.3` permanece pendente porque não houve Release.

## Stress e performance

Aprovado automaticamente:

- dez ciclos lógicos críticos load/play/pause/resume/stop sem estado obsoleto;
- cem trocas lógicas determinísticas de mídia;
- concorrência, saturação, reconfiguração, shutdown, leases copiados e dispose do framebuffer;
- fila/probe limitado com timeout e cancelamento;
- playlist/preset com 1.000 referências;
- build e testes não apresentaram hang ou deadlock.

Pendente fisicamente:

- reprodução real prolongada;
- troca repetida de mídias/codec usando LibVLC nativo;
- 720p/1080p/4K e coleta de CPU/RAM/GPU externa;
- `scripts/soak-test.ps1` sobre a build 0.10.3 em sessão exclusiva.

## Installer

- Setup x64 e x86 compilados;
- `MinVersion=10.0.17763`;
- instalação por usuário e sem elevação;
- x64 usa modo 64-bit; x86 usa arquitetura compatível correspondente;
- troca silenciosa de arquitetura é bloqueada pelo `install.json`;
- dados ficam fora da pasta do programa e devem sobreviver à desinstalação.

Instalação, atalhos, Explorer opt-in, ícones do shell, launch/close e desinstalação não foram marcados como aprovados: a máquina possui uma instalação 0.10.2 ativa e esses itens exigem inspeção humana.

## Artefatos finais locais

| Arquivo | Bytes | SHA-256 |
|---|---:|---|
| `GuiaPlay-0.10.3-prototipo-win-x64.zip` | 145.495.546 | `1ad0fe9009a4812ba74343f9e4bdfb606a6a2365f2c3028a361a83fe9211b258` |
| `GuiaPlay-0.10.3-prototipo-win-x86.zip` | 137.846.552 | `d541f89992a09ac36370f3d1e664d5eb138c7fcb0491ea6678e433884fb3085d` |
| `GuiaPlay-Setup-0.10.3-prototipo.exe` | 99.018.881 | `de28a0f2ddd80c173f7f96765e0b65c6b33b9c9f5b88423d81cfa5e1d165e3b2` |
| `GuiaPlay-Setup-0.10.3-prototipo-win-x86.exe` | 92.034.084 | `b9447935e00e975103aebfdb42c8eaa7344188ae5d18af4ad7a0e80e837b79db` |
| `SHA256SUMS.txt` | 424 | `b376a6e6d8345eb81dddddad251c7591b968dd48b4bb05620364fe35fc780972` |
| `update-manifest.json` | 952 | `ef019a4c3ada677b59e16864c6e1d7ce6e261f29c8a51ac3fab04b5f3d19e9fe` |

Conteúdo adicional validado:

- ZIP x64: 831 entradas, 332.284.703 bytes descompactados, somente LibVLC `win-x64`;
- ZIP x86: 830 entradas, 315.803.821 bytes descompactados, somente LibVLC `win-x86`;
- marcador de ambos: versão `0.10.3-prototipo` e RID correspondente;
- `package` do manifesto é idêntico a `packages.win-x64`.

## Git e Release

- branch de trabalho: `main`;
- commit de implementação: a registrar após a revisão final;
- push: a executar após a revisão final;
- tag `v0.10.3-prototipo`: **não criada**;
- GitHub Release: **não criada**;
- URL: **não aplicável enquanto o gate físico estiver aberto**;
- RC1/1.0: **não publicados**.

## Testes físicos restantes

- [ ] Windows 10 LTSC 2019/2021 real
- [ ] Windows 11 real com janela/playback da build nova
- [ ] Intel
- [ ] AMD, quando disponível
- [ ] x64 completo
- [ ] x86/WOW64 completo
- [ ] 1 monitor
- [ ] 2 saídas
- [ ] 3 monitores
- [ ] DPI 100%, 125% e 150%
- [ ] HDMI, áudio USB e áudio onboard
- [ ] Bluetooth, quando disponível
- [ ] equalizador por audição
- [ ] playback prolongado e soak
- [ ] indicador de update no startup
- [ ] update publicado `0.10.2-prototipo → 0.10.3-prototipo`
- [ ] preset Save, Load, Delete e arquivo ausente na UI real
- [ ] Explorer opt-in e sem autoplay
- [ ] instalação/launch/close/desinstalação x64
- [ ] instalação/launch/close/desinstalação x86
- [ ] preservação dos dados do usuário após uninstall

Conclusão: **engenharia automatizada verde; publicação bloqueada somente pelos gates físicos/humanos explicitamente listados**.
