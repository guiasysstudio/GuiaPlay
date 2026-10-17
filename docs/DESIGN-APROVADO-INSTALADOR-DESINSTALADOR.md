# GuiaPlay — referencias visuais aprovadas para Instalador e Desinstalador

**Status:** designs aprovados pelo responsavel em chat (17/10/2026). Registro do briefing para futuras etapas. **Nao implementar, compilar, gerar ou publicar instalador enquanto os testes do GuiaPlay Interface 3.0 estiverem pendentes.**

## Fontes visuais oficiais

- **Storyboard aprovado do instalador:** segunda montagem horizontal, oito quadros, logo do GuiaPlay, painel escuro lateral, cor de acao amarelo-alaranjada, imagens de audiovisual, fundo azul noturno. Referencia visual fornecida/gerada na conversa deste projeto, imediatamente antes de solicitar o storyboard de desinstalacao. ID do storyboard: `b161a8d9-59a5-4da8-b5b8-487e67a985d4`.
- **Storyboard aprovado do desinstalador:** segunda montagem, sete quadros, painel escuro lateral, corpo branco, acoes amarelo-alaranjadas e identidade GuiaPlay. Referencia visual da conversa, ID: `ba231671-3a74-4901-ab90-4d805d702292`.
- **Identidade visual real do produto:** `src/GuiaPlay.App/Assets/Branding/Source/GuiaPlay-Logo.svg` e `GuiaPlay-Wordmark.svg`. Esses SVGs sao a fonte de verdade da marca; nao substituir por logotipo inventado pelo mockup. Preservar composicao e hierarquia da imagem aprovada e reconciliar a paleta com os assets oficiais antes da implementacao.

## Instalador (fluxo aprovado)

1. Boas-vindas, janela horizontal e branding.
2. Licenca de uso com contrato rolavel e aceite obrigatorio.
3. Escolha do tipo: **Padrao (recomendada)** ou **Personalizada**.
   - **Padrao:** aplica pasta e componentes padrao predefinidos, sem exibir telas de pasta/componentes; prossegue diretamente para a instalacao apos avancar/confirmar.
   - **Personalizada:** apresenta diretorio de instalacao, depois selecao dos componentes e finalmente progresso.
4. Tela de diretorio **somente na Personalizada**.
5. Tela de componentes **somente na Personalizada**; programa principal sempre obrigatorio.
6. Progresso com estado das etapas reais (nao simular progresso, tamanhos ou tempo).
7. Conclusao com a unica opcao **Executar o GuiaPlay agora** (marcacao opcional). **Nao incluir Abrir pasta de instalacao**.
8. Ao clicar Concluir: se a opcao Executar estiver desmarcada, fechar instalador sem iniciar nada. Se marcada, apresentar a janela horizontal de **Boas-vindas / Programa iniciado**, com botao **Vamos comecar?**. Somente apos esse clique, fechar essa janela e o instalador, e entao iniciar o GuiaPlay; nao iniciar antes. Estado e falhas da inicializacao devem ser tratados.
9. Nenhum atalho, integracao ou opcao falsa; nao prometer componentes que o instalador nao fornece. Dimensoes e composicao **horizontais/retangulares**, nunca janelas verticais.

## Desinstalador (fluxo aprovado: interface propria)

1. Boas-vindas de desinstalacao.
2. Confirmacao com nome/versao/local reais da instalacao.
3. Opcoes de remocao claramente diferenciadas: binarios obrigatorios; atalhos e integracoes instalados; configuracoes e playlists **opcionais e preservadas por padrao**. Qualquer exclusao de dados do usuario exige consentimento explicito e inventario correto.
4. Confirmacao final com resumo antes da exclusao e aviso de irreversibilidade.
5. Progresso real da desinstalacao, sem checkmarks ficticios.
6. Conclusao e botao Concluir.
7. Pesquisa de satisfacao **opcional** apos concluir; deve ser possivel ignorar/fechar sem transmissao de dados. Se enviada, exigir informacoes claras sobre consentimento e destino.

**Regra principal:** o desinstalador nao reutiliza o wizard de instalacao; tem janelas/fluxo/textos/acoes proprios, iguais ao storyboard aprovado. Os dois compartilham apenas a identidade visual. Evitar mostrar opcoes absurdas como selecao de pasta ou aceitar licenca no desinstalador.

## Limites de execucao

O design e a documentacao sao referencias para uma proxima fase. **Nao criar nem publicar instalador/desinstalador agora.** Primeiro diagnosticar o fechamento espontaneo do app ao rodar `dotnet run` no VS Code e validar a Interface 3.0. Manter main, instalacao estavel, dados de licenca e GitHub Releases inalterados.
