# GP-YT-04 — YouTube em múltiplas telas públicas

Implementação experimental, branch `feat/gp-interface-3-modelo1`. Não publicar na versão estável sem testar em Windows com 2, 3 e 4 monitores físicos.

## Fluxo

- Adicionar um link **somente cadastra**, sem iniciar reprodução.
- Selecionar 1 ou mais telas públicas e clicar em **Reproduzir** (ou dar dois cliques no vídeo).
- É criado **um player oficial do YouTube por monitor físico selecionado**, cada janela preenchendo sua tela pública.
- A primeira tela marcada é o player primário de referência e tem áudio conforme volume/mute do GuiaPlay. Os outros players são silenciados, evitando eco.
- Play, pause, seek e stop são transmitidos a todas as saídas. Verificação de posição a cada 2 s com correção quando diferença >= 1,8 s e duração disponível. **Não é sincronização de quadros**.
- O YouTube recebe uma tentativa de autoplay somente depois do clique Reproduzir do GuiaPlay. O navegador ou YouTube podem bloquear reprodução com som; o evento `onAutoplayBlocked` precisa ser exibido, nunca ocultado.
- Nenhum player é oculto ou duplicado na tela do operador. Não se captura, extrai nem retransmite áudio/vídeo, anúncios ou quadros do YouTube.
- Um único player com autoplay por tela física, conforme regras do YouTube.

## Limitações explícitas

- A API do YouTube não fornece uma fonte única de quadros para o app distribuir como o LibVLC. Players independentes podem sofrer buffering, diferenças de tempo, propagandas diferentes e bloqueios de autoplay/embedding.
- A correção periódica é **best-effort**, não substitui vídeo com sincronização de frame. Reproduzir o mesmo YouTube em 4 players também gera 4 downloads e aumenta uso de CPU/RAM/rede.
- Para múltiplos monitores mostrando **exatamente os mesmos quadros**, a alternativa é duplicar uma saída no Windows ou usar um divisor/distribuidor de vídeo externo (HDMI/DP), com só um player real.
- Se uma tela pública ativa se desconectar ou uma das janelas for fechada, a sessão conjunta é encerrada por segurança.

## Gate físico de validação

1. Conferir `dotnet build`, `dotnet test` Debug/Release e publish smoke x64/x86.
2. Com um monitor público: verificar que a reprodução anterior continua funcionando.
3. Com 2, 3 e 4 saídas selecionadas (operador separado): conferir uma janela oficial em cada saída, nenhuma flutuante no operador.
4. Testar autoplay com som (pode ser bloqueado), mensagens de erro por tela, pausa/continuar, seek e parar todas as saídas.
5. Testar comportamento com bloqueio por idade, vídeo privado, anúncios, buffering lento, monitor desconectado e nova seleção de tela durante reprodução.
6. Testar ao menos 20 minutos: estabilidade, áudio apenas na tela primária, posicionamento dos monitores, logs sem falhas críticas.

**Não afirmar sincronismo perfeito nem autoplay garantido.** Os testes automatizados de compilação não comprovam reprodução real do YouTube.
