# Relatório M10 — Interface 2.0, Aparência, Áudio e Atualização

Versão: **0.10.0-prototipo**

Data da versão: **26/09/2026**

Canal: **Prototype**

## Escopo

O M10 moderniza a experiência visual e acrescenta personalização segura sem mudar a arquitetura de reprodução, a integração do Explorer ou o modelo de instalação por usuário. Esta entrega permanece identificada como protótipo. **Não foram criadas nem publicadas as versões `1.0.0-rc1` ou `1.0.0`.**

## Correção definitiva da consulta automática

A causa do defeito físico era a reutilização da agenda de 12 horas durante o startup: um cache recente podia encerrar a operação antes de qualquer consulta ao GitHub. Agora cada novo processo, quando a opção automática está habilitada, restaura primeiro o último indicador confiável e, depois de a janela carregar e aguardar 1,5 segundo, executa exatamente uma consulta real à API.

Um coordenador por processo compartilha a mesma tarefa entre chamadas concorrentes. A janela principal assina diretamente `UpdateManager.StateChanged`; a seta aparece somente para um estado confiável `UpdateAvailable`. Falha de rede não apaga uma disponibilidade anteriormente confirmada, e nunca bloqueia a inicialização. A janela de 12 horas continua válida apenas para novas verificações automáticas solicitadas durante o mesmo processo.

O log distingue cache restaurado, preferência desativada, consulta real, atualização encontrada, versão atualizada e falha recuperável.

## Interface 2.0

A janela principal foi reorganizada em cartões: cabeçalho e mídia atual, prévia, playlist e transporte. Abrir, reproduzir, pausar e parar permanecem ações visíveis; timeline, volume, telas e estado operacional continuam acessíveis no fluxo principal. O layout tem mínimo de 840 × 560 DIPs e foi preparado para 1366 × 768 e escalas 100%, 125% e 150%.

Configurações usa navegação lateral com as áreas **Telas**, **Aparência**, **Áudio**, **Atualizações**, **Windows**, **Diagnóstico** e **Sobre**. A estrutura usa controles WPF nativos e mantém navegação por teclado, foco e alto contraste.

## Aparência e cores de destaque

Os modos Sistema, Claro e Escuro continuam disponíveis. Sistema acompanha `AppsUseLightTheme` do Windows e reage à alteração de preferências; alto contraste sempre prevalece e usa cores do próprio sistema.

Há seis cores de destaque: Azul GuiaPlay, Ciano, Roxo, Verde, Laranja e Rosa. Cada uma possui variante clara e escura com foreground e superfície sutil próprios. A página apresenta uma prévia antes de salvar. O schema 6 de `settings.json` migra versões anteriores para Azul GuiaPlay e preserva chaves desconhecidas.

## Equalizador nativo do LibVLC

O recurso usa exclusivamente `LibVLCSharp.Equalizer` e `MediaPlayer.SetEqualizer`/`UnsetEqualizer`; não há DSP externo, serviço ou processo residente. Presets e frequências são descobertos em runtime, portanto a interface não promete uma lista inexistente em outra versão do motor.

Com LibVLC 3.0.24 para Windows foram descobertos 18 presets reais: Flat, Classical, Club, Dance, Full bass, Full bass and treble, Full treble, Headphones, Large Hall, Live, Party, Pop, Reggae, Rock, Ska, Soft, Soft rock e Techno. Foram descobertas dez bandas: 31,25; 62,5; 125; 250; 500; 1.000; 2.000; 4.000; 8.000 e 16.000 Hz.

O equalizador começa desativado. O usuário pode escolher preset, preamp e ganhos entre -20 e +20 dB; qualquer ajuste manual passa a **Personalizado**. A configuração é normalizada e persistida no schema 6, aplicada imediatamente à sessão corrente e reaplicada a novas mídias ou a uma sessão recriada por troca de áudio. Uma falha opcional no equalizador é registrada e informada sem impedir a reprodução original.

## Compatibilidade preservada

Continuam inalterados:

- uma instância por Mutex + Named Pipe e carregamento externo sem autoplay;
- integração Explorer opt-in, ProgIDs, Capabilities e desinstalação seletiva;
- updater com manifesto, SHA-256, staging, backup e rollback;
- diagnóstico, backpressure, proteção contra callbacks antigos e scripts de soak test do M09;
- instalação per-user, sem administrador, com execução final desmarcada.

## Testes

A cobertura M10 inclui consulta real mesmo com cache recente, uma consulta por processo sob concorrência, preferência desativada, indicador restaurado enquanto a rede responde, retenção offline de update conhecido, resolução Claro/Escuro/Sistema/alto contraste, seis paletas, migração para schema 6, round-trip de aparência/equalizador, limites numéricos, presets e modo Personalizado.

Também há verificações estruturais da nova janela, navegação de Configurações, conexão direta do estado de update e chamadas nativas do equalizador. Os testes M09 de stress e todos os testes anteriores permanecem na matriz.

O pipeline oficial `scripts/build-release.ps1` concluiu com sucesso:

- `dotnet format --verify-no-changes`: aprovado;
- build Debug e Release: 0 erros e 0 warnings;
- testes Debug: 203/203 aprovados;
- testes Release: 203/203 aprovados;
- stress de trocas/IPC/1.000 itens/startup concorrente: 40/40 em dez repetições;
- ZIP: `GuiaPlay-0.10.0-prototipo-win-x64.zip` (240.089.419 bytes);
- Setup: `GuiaPlay-Setup-0.10.0-prototipo.exe` (151.152.370 bytes).

SHA-256 conferido independentemente após a montagem:

- ZIP: `5a272f3cd684d8ae66c0560d661bd1d0e8365f0b2346fb5a60bb9c4ce084beb2`;
- Setup: `54ba64cde3f80916c7a798c490c36bfdb8e057bb2cfd57a0b0dcdb751a94f48e`.

O ZIP contém 1.667 entradas e `GuiaPlay.exe`; o executável informa `ProductVersion` **0.10.0-prototipo** e `FileVersion` **0.10.0.0**.

A inspeção visual automatizada de uma janela nativa não estava disponível no ambiente e a instalação 0.9 já aberta reteve o Mutex; por segurança ela não foi encerrada. Assim, contraste, DPI, áudio real, reprodução prolongada e o fluxo físico de atualização continuam no roteiro de Andrew, sem alegação de validação não executada.
