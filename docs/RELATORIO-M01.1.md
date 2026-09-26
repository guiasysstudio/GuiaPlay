# Relatório de implementação — M01.1

Data: 25/09/2026  
Versão: 0.1.1-prototipo

## Retorno manual recebido

Andrew informou que a reprodução do M01 está funcionando normalmente nas telas disponíveis. Este relatório registra o retorno como validação manual básica do fluxo de reprodução. Ele não é tratado como medição de CPU/GPU, cobertura de falhas, validação 4K ou comprovação de sincronismo físico perfeito entre televisores/projetores.

## Causa encontrada

O M01 misturava superfícies nativas claras com cores escuras fixas. Um estilo global de `TextBlock` forçava texto branco também dentro dos templates de `ComboBox` e botões, enquanto os fundos internos podiam permanecer claros. A janela e vários textos ainda tinham cores hexadecimais fixas, impedindo adaptação coerente ao tema.

O identificador anterior era uma janela do tamanho integral do monitor com fundo escuro e um bloco de 260 DIPs no centro; por isso encobria todo o conteúdo subjacente.

## Solução

- Ativado o tema Fluent nativo do WPF/.NET 10 por `Application.ThemeMode`, com Sistema, Claro e Escuro.
- `ThemeMode` existe no Windows Desktop 10, mas ainda traz o marcador experimental `WPF0001`; a supressão foi limitada ao projeto WPF. A escolha é adequada ao protótipo e fica registrada para reavaliação antes do empacotamento.
- Removidas as cores globais conflitantes; controles usam templates, estados e recursos dinâmicos do tema.
- Sistema acompanha o modo de aplicativos do Windows. O Fluent assume o tema de alto contraste e restaura a preferência anterior quando ele termina.
- A preferência fica isolada em `%LocalAppData%\GuiaSys\GuiaPlay\settings.json`; ausência, JSON inválido ou valor desconhecido retornam para Sistema. Chaves futuras são preservadas ao salvar.
- O painel ganhou espaçamento uniforme, destaque nativo para Reproduzir, lateral rolável, dimensões mínimas menores e tempos/volume em colunas estáveis.
- Arquivo mostra nome curto e caminho em linha/tooltip. Monitores mostram `Tela N — DISPLAYN` e uma segunda linha com resolução, posição e escala.
- Cada identificador agora é uma janela de 112 DIPs, sem borda/tarefa/Alt+Tab, com `WS_EX_NOACTIVATE`, posicionada por pixels físicos a 24 DIPs do canto inferior esquerdo da área útil.
- Cliques repetidos cancelam o prazo anterior, substituem as janelas e reiniciam os três segundos. Esc no painel fecha apenas identificadores. Mudança de topologia os fecha com segurança.
- O pipeline LibVLC, os buffers e as regras de reprodução não foram alterados.

## Arquivos principais alterados

- `App.xaml` e `App.xaml.cs`: tema e aplicação da preferência.
- `MainWindow.xaml` e `MainWindow.xaml.cs`: layout, seletor, rótulos e ciclo dos identificadores.
- `AppearanceSettings.cs`: persistência mínima testável.
- `MonitorInfo.cs`, `MonitorService.cs` e `WindowPlacement.cs`: rótulos, área útil, DPI e posicionamento.
- `IdentifierWindow.cs`: identificador compacto e alto contraste.
- testes, README, requisitos, backlog e roteiro manual.

## Verificações executadas

- API `ThemeMode` confirmada na documentação oficial do WPF para Windows Desktop 10.
- Build Debug aprovado sem avisos ou erros.
- Build Release aprovado sem avisos ou erros.
- 12/12 testes Debug e 12/12 testes Release aprovados: 7 regras de reprodução e 5 casos de aparência/persistência.
- `dotnet format --verify-no-changes` aprovado.
- Inicialização real Debug aprovada: processo responsivo, janela principal criada com título `GuiaPlay 0.1.1-prototipo` e log de painel carregado.
- Smoke Release aprovado: produto `0.1.1-prototipo`, arquivo `0.1.1.0` e processo x64 responsivo.
- A preferência foi verificada em diretório temporário pelos testes para Sistema/Claro/Escuro, configuração ausente/inválida e preservação de chaves desconhecidas.

## Pendências visuais reais

O conector de controle visual do Windows retornou inventário vazio nesta sessão, mesmo com a janela principal aberta. Portanto, build e smoke de processo **não** são apresentados como prova visual.

Andrew ainda precisa conferir no computador alvo:

- contraste em Claro/Escuro/Sistema e todos os estados dos controles;
- reação ao tema do Windows e alto contraste ao vivo;
- popup do ComboBox, tooltips, textos longos, 1366 × 768 e escalas DPI usadas na igreja;
- posição/tamanho/foco/Alt+Tab dos identificadores nos monitores físicos;
- repetição, prazo de três segundos, Esc e desconexão durante identificação;
- mudança de tema e identificação durante reprodução, sem interrupção ou áudio duplicado.

O roteiro atualizado contém os passos exatos. Nenhum vídeo pessoal foi pesquisado e nenhuma configuração global do Windows foi modificada.

## Referências técnicas conferidas

- [Novidades do WPF no .NET 10](https://learn.microsoft.com/dotnet/desktop/wpf/whats-new/net100)
- [Tema Fluent e ThemeMode no WPF](https://learn.microsoft.com/dotnet/desktop/wpf/whats-new/net90)
- [Window.ThemeMode no Windows Desktop 10](https://learn.microsoft.com/dotnet/api/system.windows.window.thememode?view=windowsdesktop-10.0)
- [Estilos e templates WPF](https://learn.microsoft.com/dotnet/desktop/wpf/controls/styles-and-templates)
- [SystemParameters.HighContrast](https://learn.microsoft.com/dotnet/api/system.windows.systemparameters.highcontrast)
