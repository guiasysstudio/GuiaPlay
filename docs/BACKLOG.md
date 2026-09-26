# Backlog por marcos

## M01 — 0.1.0-prototipo

Protótipo funcional multitelas, prévia/controles, motor compartilhado por callbacks, ciclo de vida seguro, testes da lógica, logs e roteiro de validação. Implementado nesta entrega; validação física continua pendente conforme o relatório.

## M01.1 — 0.1.1-prototipo

- Tema Fluent nativo do WPF com opções Sistema, Claro e Escuro.
- Persistência mínima e isolada da aparência, antecipada deliberadamente do M02.
- Correção de contraste de controles, acabamento responsivo e rótulos curtos de monitores.
- Identificadores compactos no canto inferior esquerdo da área útil, sem cobertura da tela.
- O retorno manual de Andrew informa que a reprodução do M01 funciona normalmente nas telas disponíveis; esse relato não equivale a medição de desempenho ou prova de sincronismo físico perfeito.

## M02 — 0.2.0-prototipo

- Implementado armazenamento versionado e atômico no `settings.json` existente para aparência, telas, operador, pré-seleção pública, áudio, volume e mudo.
- Implementados nomes personalizados, configuração Salvar/Cancelar e identidade persistente por caminho de dispositivo do Windows, sem restauração automática ambígua.
- Implementada seleção real de saída pelo par módulo/dispositivo aceito pelo LibVLC, incluindo Padrão do Windows, estado indisponível sem fallback e pausa em perda detectada.
- Implementadas regras de operador provisório, desconexão/reconexão sem reautorização e reposicionamento do painel na área útil/DPI da tela.
- Acrescentados testes de migração, falhas, reconciliação de telas, identidade de áudio e eventos antigos.
- Diagnóstico amigável de desempenho e eventual pipeline GPU/plugin nativo continuam dependentes dos dados de validação prolongada no computador alvo; não foram ampliados neste marco.

## M03 — 0.3.0-prototipo

- Interface focada na operação ao vivo, com configurações permanentes na engrenagem e controles compactos na faixa inferior.
- Playlist/cronograma com grupos virtuais, referências a arquivos, ordem persistida e sinalização de original ausente.
- Suporte operacional a vídeo e áudio comuns do LibVLC, sem cópia/importação de mídia.
- Política central: vídeo exige saída pública; áudio independe de telas e nunca cria/ativa saída de vídeo.
- Reprodução imediata sem confirmação intermediária e duplo clique na playlist seguindo a mesma política.
- Persistência separada e atômica da playlist, schema 3 de configurações e cobertura automatizada ampliada.

## M04 — 0.4.0-prototipo

- Correções visuais do diálogo de grupo e indicador estático do operador.
- Ícones locais Microsoft Fluent System Icons, sem biblioteca de renderização.
- Arrastar referências do Explorer e reordenar grupos/itens, inclusive entre grupos.
- Argumento de linha de comando e instância única com Mutex + Named Pipe.

## Próximos marcos — distribuição

- Abrir pelo Explorer após associação opt-in feita pelo usuário.
- Publicação/empacotamento x64 e política de atualização.
- Validação integrada prolongada no computador da igreja, incluindo recuperação de falhas e matriz de formatos.

## Não planejado para estes marcos

Streaming, YouTube, projeção de letras, contas, nuvem, licenciamento e captura de outras aplicações.
