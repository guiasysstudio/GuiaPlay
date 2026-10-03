# Release gate — GuiaPlay 1.0

Este gate impede que `1.0.0-rc1` ou `1.0.0` seja publicado apenas porque o build automatizado ficou verde. A versão deste marco continua sendo `0.10.3-prototipo`.

## Bloqueadores absolutos

Não publicar se houver qualquer crash conhecido, deadlock conhecido, perda de configuração/playlist, update sem rollback, pacote de arquitetura divergente, instalador inválido ou resultado físico obrigatório sem evidência.

## Gate automatizado

- [ ] `dotnet restore GuiaPlay.slnx`
- [ ] `dotnet format GuiaPlay.slnx --verify-no-changes --no-restore`
- [ ] build Debug: 0 warnings / 0 errors
- [ ] testes Debug: Core e App verdes
- [ ] build Release: 0 warnings / 0 errors
- [ ] testes Release: Core e App verdes
- [ ] publish self-contained win-x64 com EXE, updater, LibVLC, plugins e assets
- [ ] publish self-contained win-x86 sem DLL nativa x64 misturada
- [ ] ZIP x64/x86 e instaladores correspondentes
- [ ] manifesto legado `package` continua apontando para x64
- [ ] `packages` seleciona estritamente win-x64 ou win-x86
- [ ] SHA-256 local dos artefatos validado
- [ ] SemVer e canais Prototype/RC/Stable cobertos
- [ ] stress automatizado de concorrência/teardown sem hang
- [ ] preset de 1.000 referências salva e carrega preservando ordem
- [ ] branding regenerável e SVGs obrigatórios presentes

Marcar cada item somente no relatório da execução que produziu os artefatos. Não reutilizar marca de uma versão anterior.

## Gate físico e humano

- [ ] Windows 10 LTSC 2019 ou 2021 real
- [ ] Windows 11 real
- [ ] PC Intel
- [ ] PC AMD, quando disponível
- [ ] win-x64
- [ ] win-x86 em Windows 10 x86 ou WOW64
- [ ] 1 monitor
- [ ] 2 saídas
- [ ] 3 monitores
- [ ] DPI 100%
- [ ] DPI 125%
- [ ] DPI 150%
- [ ] HDMI
- [ ] áudio USB
- [ ] áudio onboard
- [ ] Bluetooth, quando disponível
- [ ] equalizador validado por audição
- [ ] playback prolongado
- [ ] indicador de update no startup
- [ ] update real `0.10.2-prototipo → 0.10.3-prototipo`
- [ ] preset Save
- [ ] preset Load
- [ ] preset Delete
- [ ] preset com arquivo ausente
- [ ] Explorer opt-in e sem autoplay
- [ ] instalação x64
- [ ] desinstalação x64 preservando dados do usuário
- [ ] instalação/desinstalação x86, se publicada

## Aprovação

| Papel | Nome | Data | Evidência |
|---|---|---|---|
| Engenharia |  |  |  |
| Operação/igreja |  |  |  |
| Release |  |  |  |

Enquanto qualquer item obrigatório permanecer sem evidência, a tag `v1.0.0-rc1` e a Release correspondente não devem ser criadas.
