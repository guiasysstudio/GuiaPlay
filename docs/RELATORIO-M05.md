# Relatório M05 — Sistema de Atualização e Distribuição

Versão: **0.5.0-prototipo**

Data da versão: **25/09/2026**

Canal: **Prototype**

## Entrega

O M05 centraliza versão, data, canal e repositório em metadados de build; adiciona comparação semântica leve; consulta assíncrona e estruturada à API de Releases do GitHub; persistência do intervalo de 12 horas; nova área **Atualizações** nas configurações; indicador condicional na janela principal; download em streaming; validação SHA-256; extração ZIP protegida contra path traversal; modo de instalação gerenciada; updater externo com backup/rollback; publish self-contained win-x64; instalador Inno Setup por usuário; manifest, checksums e scripts reprodutíveis.

## Arquitetura

- `ProductInfo` lê `InformationalVersion`, data, canal e repositório dos metadados gerados por `Directory.Build.props`.
- `ProductVersion` interpreta tags opcionais com `v` e compara componentes numéricos; versão final tem precedência sobre prerelease do mesmo número.
- `GitHubUpdateService` usa a listagem `/repos/{owner}/{repo}/releases`, ignora drafts e tags inválidas e considera prereleases `-prototipo` no canal atual. Não usa `/releases/latest`.
- `UpdateManager` mantém um `HttpClient` compartilhado, timeout de 20 segundos, User-Agent do produto e estados consumidos pela UI.
- `UpdatePackageDownloader`, `PackageIntegrity` e `SafeZipExtractor` validam o pacote antes de tocar na instalação.
- `ManagedInstallationDetector` exige `install.json` válido. O arquivo só é criado pelo Setup; builds de desenvolvimento não são elegíveis.
- `GuiaPlay.Updater` é publicado self-contained/single-file, copiado para `%TEMP%\GuiaPlayUpdater\<guid>` e executado fora da pasta instalada. Ele aguarda o processo principal, cria backup, aplica, faz rollback em erro e reinicia.

```text
GitHub Release -> manifest -> download -> SHA-256 -> staging seguro
                                               |
GuiaPlay instalado -> updater temporário -> backup -> aplicação -> reinício
                                                \-> erro -> rollback
```

Settings e playlist continuam em `%LocalAppData%\GuiaSys\GuiaPlay`, fora de `%LocalAppData%\Programs\GuiaPlay`. O updater preserva `install.json` e nunca interpreta comandos ou nomes de destino provenientes das release notes.

## Política operacional

A checagem automática ocorre após a janela abrir, sem bloquear startup, e no máximo a cada 12 horas. Falha automática é registrada sem interferir na operação; falha manual aparece inline. Instalação automática começa desativada, requer instalação gerenciada e é adiada enquanto áudio ou vídeo estiver ativo. O ícone de download fica `Collapsed` em todos os estados exceto `UpdateAvailable` e abre diretamente a guia de atualizações.

## Distribuição

`scripts/build-release.ps1` limpa apenas `artifacts/`, restaura, verifica formatação, compila/testa Debug e Release, publica aplicativo e updater, cria ZIP, manifesto e marcador, compila o Setup e calcula SHA-256. O aplicativo principal é self-contained win-x64, sem trimming e sem single-file para reduzir risco com WPF/LibVLC. O Setup usa `PrivilegesRequired=lowest`, instala em `%LocalAppData%\Programs\GuiaPlay`, registra desinstalação, cria atalho no Menu Iniciar e oferece atalho opcional na Área de Trabalho.

`scripts/publish-release.ps1` exige `main`, árvore limpa, origin oficial, autenticação, sincronismo remoto, assets e hashes válidos e inexistência da tag/release. Não força push nem sobrescreve publicação.

## Segurança e limitações

- Sem token, PAT ou segredo embarcado.
- HTTPS, API versionada e User-Agent explícito.
- SHA-256 obrigatório e pacote inválido removido.
- Rejeição de Zip Slip e nomes de asset com caminho.
- Backup não é apagado antes de a aplicação nova ser iniciada; pode exigir limpeza posterior.
- A infraestrutura está pronta, mas o primeiro upgrade público real depende de uma release posterior a `0.5.0-prototipo`.
- Assinatura Authenticode fica fora do M05.

## Evidências automatizadas

A suíte cobre precedência de versão, seleção de release, draft/tag inválida, downgrade, HTTP/timeout/JSON, assets/manifest, SHA, configurações, agenda de 12 horas, indicador, bloqueio por reprodução, modo gerenciado, Zip Slip e aplicação/rollback em diretórios temporários. Os números finais e hashes publicados ficam registrados nas notas da Release e no resumo do checkpoint.
