# GP-I13 — Reconstrução visual do Modelo 1 (em validação)

Data: 17/10/2026. Branch exclusiva: `feat/gp-interface-3-modelo1`. Base: `f1ed293`.
Referência visual: **segunda imagem aprovada pelo operador**, enviada em 17/10/2026 (1536 × 1024).
O objetivo é convergir para a referência, não apresentar uma compilação não executada como entrega aprovada.

## Alterações efetivamente versionadas

- Janela WPF com `WindowChrome` para integrar barra superior, logo oficial, navegação, licença e três botões reais de janela (minimizar, maximizar/restaurar e fechar). O fechamento mantém o handler existente de encerramento.
- Reorganização de proporções da área de trabalho e colunas: biblioteca à esquerda; prévia e programa lado a lado no topo; saída de vídeo e áudio em cartões independentes à direita; tabela de mídias com `Grid.ColumnSpan=3` ocupando toda a região central + direita.
- Barra inferior panorâmica com transporte de mídia central, botão principal circular com brilho e controles anteriores, ±5 s, pausa, próximo, parar, volume, repetição e aleatório. Comandos reusam os handlers existentes.
- Aplicação vetorial de contornos extraídos dos SVGs recebidos de `GuiaPlay-Icons.zip` nos controles reais, com cores de pincéis WPF adaptáveis ao tema.
- Três estados de volume em dois locais: **0% ou mudo → mute; 1–80% → down; 81–100% → up**. A banda intermediária segue a alternativa sugerida para não haver intervalo sem ícone; confirmar visualmente com o operador.
- A arte de mídia do transporte aceita thumbnail **somente quando houver uma miniatura verificada da própria mídia**. O fallback exibe o tipo áudio/vídeo. Não inserir dados ou imagens do mockup como conteúdo real.
- Preservados `x:Name` das 86 referências anteriores do arquivo XAML, eventos de comandos, entidades e lógica de licença, atualização e reprodução.
- Testes estáticos da interface atualizados para o novo contrato do GP-I13; **não equivale a teste executado**.

## Gates ainda pendentes

1. **CI Windows real:** restore, build Debug/Release, testes xUnit, publicação smoke x86/x64. Este ambiente conectado ao GitHub não tem SDK .NET local; não afirmar build verde antes dos resultados.
2. **Smoke físico no PC do operador:** login/licença, abertura de janela, minimizar/maximizar/arrastar/redimensionar, vídeo/áudio/YouTube, saídas públicas e audio routing, teclado/mouse, Windows 10/11, DPI.
3. **Comparação visual por captura** em 1536×1024 e também janela maximizada do computador do operador. Corrigir diferenças de pixels/densidade da interface observadas no Windows real; não declarar equivalência pixel-perfect sem screenshot.
4. **Miniaturas/duração/resolução reais nas listas:** serviço de metadados permanece propositalmente gerenciado e com fallback; a antiga sondagem nativa em background causava risco de APPCRASH. Projeto de processo auxiliar isolado é necessário antes de recuperar metadados avançados e thumbnails de maneira segura.
5. Conferir responsividade DPI 100/125/150 e resoluções menores sem controles truncados. Tamanhos mínimos atuais precisam de ajuste após validação real.
6. Revisar origem/licenças de redistribuição de todos os arquivos SVG recebidos antes de empacotar assets de terceiros na versão comercial.

## Garantias

- Nenhuma mudança de `main`, código servidor de licenciamento, chave do usuário, DPAPI, arquivos locais, presets, instalador ou GitHub Release.
- Sem alteração do serviço nativo `MediaGridMetadataService` ou LibVLC nesta fase.
- SVGs foram usados como **geometrias vetoriais WPF**, não como bitmaps ou iframes.
- Referência visual contém conteúdo demonstrativo; dados reais do usuário prevalecem.

## Comandos para validar pelo VS Code

```powershell
git switch feat/gp-interface-3-modelo1
git pull --ff-only origin feat/gp-interface-3-modelo1
dotnet restore .\GuiaPlay.slnx
dotnet build .\GuiaPlay.slnx -c Debug
dotnet test .\GuiaPlay.slnx -c Debug
dotnet run --project .\src\GuiaPlay.App\GuiaPlay.App.csproj -c Debug
```

**Status: IN PROGRESS.** Não publicar instalador até os gates concluídos.
