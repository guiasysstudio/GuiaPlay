# Presets de playlist

O GuiaPlay salva diferentes roteiros de culto como snapshots de metadados. Nenhuma mídia é copiada, movida, renomeada ou excluída.

## Local e formato

Pasta padrão:

```text
%LocalAppData%\GuiaSys\GuiaPlay\playlist-presets\
```

Cada preset ocupa um arquivo `<guid>.json`. O nome visível nunca é usado como nome de arquivo. Schema atual: `1`.

```json
{
  "schema": 1,
  "id": "6b3ef1c5-8fbd-4bf1-b06f-5f1fcf274b95",
  "name": "Culto Domingo Noite",
  "createdAt": "2026-10-02T22:00:00-03:00",
  "updatedAt": "2026-10-02T22:15:00-03:00",
  "playlist": {
    "schemaVersion": 1,
    "groups": []
  }
}
```

O snapshot preserva IDs, nomes e ordem de grupos; IDs, nomes, tipo, ordem e caminho absoluto original de cada item. Arquivos ausentes permanecem referenciados e aparecem como indisponíveis.

## Salvar

- O nome é obrigatório, recebe `Trim` e aceita de 1 a 80 caracteres.
- Comparação de duplicidade é case-insensitive.
- Quando a lista veio de um preset, o diálogo começa com seu nome.
- Nome já existente exige confirmação explícita antes de sobrescrever.
- A gravação usa arquivo temporário, flush e rename no mesmo diretório; arquivos temporários não terminam em `.json` e nunca entram na biblioteca.
- `createdAt` é preservado no overwrite e `updatedAt` é atualizado.

## Carregar e excluir

O seletor mostra nome e última atualização. Carregar, Enter ou duplo clique substitui apenas a playlist de trabalho, persiste `playlist.json`, refaz a árvore e dispara nova verificação de disponibilidade. Não inicia reprodução e não altera volume, áudio, telas, equalizador ou aparência.

Excluir pede confirmação e remove somente o JSON identificado pelo GUID. Mídias originais nunca são tocadas.

Um preset corrompido, com schema incompatível, ID divergente ou nome inválido gera warning e é isolado; os demais continuam disponíveis. Itens inválidos dentro de uma playlist válida são ignorados individualmente pelo codec tolerante.

## Alterações não salvas

A área de trabalho fica dirty ao criar, renomear, excluir ou reordenar grupos; adicionar, remover, mover ou reordenar itens. Antes de carregar outro preset:

- **Sim / Salvar…** abre o fluxo de gravação e só continua se ele terminar com sucesso;
- **Não / Continuar sem salvar** descarta somente as alterações da lista de trabalho;
- **Cancelar** mantém tudo como está.

Depois de salvar ou carregar, a nova baseline fica limpa. A playlist corrente continua sendo persistida separadamente em `playlist.json`, portanto reiniciar o aplicativo restaura o trabalho mesmo sem abrir o preset.

## Recuperação e diagnóstico

- `playlist.json` parcialmente corrompido preserva grupos e itens válidos e registra warning.
- Cada preset é lido isoladamente; um JSON ruim não apaga ou bloqueia os demais.
- Arquivos com nome que não seja GUID são ignorados.
- Duplicidade de nome encontrada no disco mantém o primeiro preset determinístico e registra warning para o segundo.
- Falha de escrita não promove o snapshot como salvo nem limpa o dirty state.

O log fica em `%LocalAppData%\GuiaSys\GuiaPlay\GuiaPlay.log` e não inclui bytes das mídias.
