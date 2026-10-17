# GP-YT05 — Início do espelhamento seguro do YouTube

Data: 17/10/2026. Branch: `feat/gp-interface-3-modelo1`.
Status: **etapa 1: detecção + pré-validação sem escrita, autoplay com fallback**.

## Problema confirmado nos testes reais
O operador consegue reproduzir YouTube em uma ou duas telas públicas, mas
players independentes do WebView2 ficam fora de sincronia (buffering, anúncios,
latência). Além disso, a política de autoplay do navegador pode impedir o
início automático com som.

## Nova direção
- **Vídeos locais:** não alterar LibVLC, pipeline de quadros, quantidade de
  saídas nem transporte do operador.
- **YouTube:** um único player oficial visível em uma tela lógica, reproduzido
  por dois ou mais dispositivos físicos **quando o Windows já estiver em
  modo de duplicação das saídas públicas**.
- O painel do operador permanece estendido/independente.
- Não capturar nem retransmitir os quadros do YouTube.
- Não usar `SetDisplayConfig(SDC_APPLY)` antes de passar pelos testes de
  topologia, restauração e recuperação de falhas no hardware real.

## Implementado nesta etapa
1. `MonitorService.GetActiveCloneGroups()`: usa apenas
   `QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS)` e identificadores de origem/
   destino para descobrir grupos já duplicados. Um grupo tem uma tela
   lógica (GDI) e mais de um destino físico.
2. `MonitorService.ValidateProposedPublicClone(...)`: cria uma cópia da
   topologia atual e chama **somente** `SetDisplayConfig(SDC_VALIDATE |
   SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_ALLOW_CHANGES)`. Avalia a clonagem
   das saídas selecionadas mantendo a origem do operador separada;
   validação não muda monitores nem comprova recuperação pós-crash.
   Recusa fontes não identificáveis e adaptadores públicos distintos.
3. Botão **Espelhar** na seção Saída de vídeo: consulta duplicações já
   ativas; se duas ou mais saídas públicas estiverem selecionadas, exibe
   resultado da pré-validação. Permite abrir `ms-settings:display` para
   configuração manual do Windows. **Não altera a topologia sozinho.**
4. Com duplicação já configurada, o GuiaPlay reconhece quantas telas
   físicas compartilham a origem e mantém **um player YouTube oficial**
   para cada tela lógica escolhida, sem duplicar players para os
   dispositivos físicos espelhados.
5. Autoplay após o comando Reproduzir: tenta iniciar com som. Se
   `onAutoplayBlocked` ocorrer, faz **uma única** nova tentativa sem som,
   mantendo mensagem no painel. Se o navegador bloquear também a
   tentativa silenciosa, exige clique direto no player. Sem browser flags
   de bypass nem promessas de áudio automático garantido.

## Importante: o que AINDA NÃO está implementado
- Trocar automaticamente a topologia do Windows e depois restaurá-la.
- Recuperar topologia original depois de queda do aplicativo, troca de
  cabos, resolução ou perda do monitor do operador.
- Garantir autoplay com áudio ou sincronizar players independentes.
- Migração para a versão estável ou instalador de produção.

## Testes físicos obrigatórios
1. Na versão Debug, marcar os projetores 2 e 3 e clicar **Espelhar**.
   Anotar a mensagem de pré-validação; confirmar que não mudou nada.
2. Se a pré-validação for adequada, abrir Configurações do Windows e
   configurar MANUALMENTE duplicação entre as telas 2 e 3; manter o
   operador independente. A identificação numérica pode mudar.
3. Voltar ao GuiaPlay, reconfigurar o operador se necessário, selecionar
   a única tela lógica do grupo duplicado e reproduzir YouTube.
   Confirmar mesmas imagens nos dois projetores, um único player.
4. Testar Play/Pause/Stop e autoplay; se ficar sem áudio, capturar
   mensagens `GP-YT05` no log e verificar se clique manual no player
   permite desmutar. Não tentar forçar restrições do navegador.
5. Verificar vídeos locais MP4 em todas as saídas depois de restaurar o
   modo estendido normal e verificar licenciamento intacto.

**Próximo gate:** apenas após obter o resultado da pré-validação e
confirmar o hardware permitir duplicação seletiva, estudar a troca
automática com snapshot durável, confirmação humana, timeout de
restauração e rollback após crash. Não usar `SDC_TOPOLOGY_CLONE`
global, pois isso pode duplicar também a tela do operador.
