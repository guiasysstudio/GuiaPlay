# GP-I02 — Biblioteca lateral / playlists salvas

Data de implementação: 2026-10-16  
Base técnica: GuiaPlay 1.0.3 Stable  
Branch exclusiva: `feat/gp-interface-3-modelo1`  
Situação: **implementado no código, pendente de compilação e validação integrada no Windows (GP-I10).**

## Entregáveis implementados

1. **Biblioteca de playlists salvas** na parte superior da coluna esquerda, alimentada pelos JSONs reais de `PlaylistPresetStore`; não há listas fictícias.
2. Cada playlist mostra nome, número de grupos, número de mídias e data de última atualização do preset; a playlist ativa mostra `ATIVA` ou `EDITANDO` (se houve alterações desde salvar).
3. Ações `Nova`, `Salvar`, `Abrir` e `Excluir`: salvar utiliza o prompt e tratamento de conflito existentes; abrir usa carregamento a partir do arquivo atual do preset; excluir requer confirmação e remove somente o JSON do preset, nunca a mídia nem a playlist de trabalho.
4. Duplo clique em um item salvo executa o mesmo fluxo do botão Abrir. Botões sem seleção ficam desabilitados.
5. Ação **Nova playlist**: confirma antes de substituir uma lista existente; se há alterações não salvas, oferece `Salvar / Continuar sem salvar / Cancelar`. A lista vazia é persistida atomicamente antes de ser adotada em memória. A biblioteca de presets existentes é mantida.
6. O código de `PlaylistWorkspace.StartNew` zera exclusivamente a lista de trabalho e sua associação ao preset ativo, exige descarte explícito se o catálogo estiver `IsDirty`.
7. Abaixo das playlists, painel **PASTAS / GRUPOS**, explicitamente identificado como **grupos virtuais da lista atual**. Mantém a árvore real de grupos e itens, criação/renomeação/exclusão, importação de arquivos individuais, remoção de referência e arrastar/soltar já implementados.
8. Estados vazios para biblioteca e lista de trabalho, aviso quando presets inválidos forem encontrados e contadores reais de grupos/mídias.
9. Cor e ícones reutilizam recursos semânticos do tema atual e estilo de seleção/hover/foco para itens de biblioteca.
10. Nova UI continua dentro de `LicensedWorkspace`, portanto respeita bloqueio de licenciamento; comandos próprios de carregar/excluir/nova também verificam estado autorizado e `_controlsBusy`.
11. Ao confirmar salvamento de alterações durante a troca de preset, o destino é relido do disco antes de carregar, evitando substituir dados por snapshot anterior à confirmação.

## Arquivos alterados/criados

- `src/GuiaPlay.App/MainWindow.xaml`: nova biblioteca de presets e painel de grupos com ações reais.
- `src/GuiaPlay.App/MainWindow.xaml.cs`: bindings, handlers, confirmação de dirty, nova playlist, atualização de lista, estados, abertura/exclusão e persistência.
- `src/GuiaPlay.App/Models/SavedPlaylistEntry.cs`: modelo de apresentação, sem persistência nova.
- `src/GuiaPlay.Core/PlaylistPresets.cs`: operação `StartNew` do workspace.
- `tests/GuiaPlay.Core.Tests/PlaylistPresetTests.cs`: dois novos casos de teste **escritos, ainda não executados** (dirty guard; proteção de arquivos e presets).

## Escopo que propositalmente pertence a etapas seguintes

- **GP-I03:** grade tabular profissional com miniaturas e metadados; ao concluir, decidir a melhor posição da lista de trabalho e dos grupos.
- **GP-I04:** importação verdadeira de diretórios/subpastas do sistema de arquivos; o painel GP-I02 mostra apenas **grupos virtuais**, não simula diretórios físicos.
- **GP-I05 a GP-I09:** layout de prévias/programa, controles, teclado, YouTube.
- **GP-I10:** compilações Debug/Release, suíte de testes, teste real pelo VS Code, smoke em monitores e regressões.

## Inspeção estática efetuada

- Tags XAML balanceadas na versão GP-I02; recursos estáticos/dinâmicos declarados; métodos referenciados por eventos presentes em `MainWindow.xaml.cs`; sem nomes `x:Name` duplicados.
- Não houve compilação ou execução do software nem de testes automatizados, conforme regra expressa de concluir as onze etapas primeiro.
- Nenhum instalador, arquivo de distribuição, tag, release, deploy ou alteração de `main`.

## Riscos a validar no GP-I10

- Ajustes finais de responsividade dos dois painéis laterais nas resoluções pequenas e em DPI elevado.
- Interação de seleção/duplo clique, atualização dos contadores e estados após salvar/carregar/excluir.
- Recuperação de preset parcial/corrompido e falha de escrita no `playlist.json`.
- Transição de biblioteca/playlist durante reprodução, sem mudança na mídia já em execução ou perda da autorização do operador.
