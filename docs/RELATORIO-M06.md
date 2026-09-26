# Relatório M06 — Identidade Visual, Instalador Personalizado e Primeiro Update Real

Versão: **0.6.0-prototipo**

Data da versão: **26/09/2026**

Canal: **Prototype**

## Entrega

O M06 aplica a identidade oficial fornecida ao executável e à interface, personaliza o instalador Inno Setup sem substituir a arquitetura do M05, acrescenta uma seção **Sobre** e prepara a primeira atualização pública real entre versões distintas.

## Aplicativo

- `GuiaPlay.ico` é o `ApplicationIcon` do projeto WPF e também o recurso usado pelas janelas principal e de Configurações.
- `GuiaPlay-Wordmark-UI.png` é incorporado como `Resource`; não depende de caminho absoluto e não é copiado solto no publish.
- O cabeçalho principal mantém o nome da mídia como informação operacional prioritária, com wordmark discreto à esquerda e ações de update/configuração à direita.
- Configurações mantém a área de Atualizações do M05 e adiciona uma guia **Sobre** leve.
- Não foi adicionada splash screen; o tempo de abertura continua prioritário.

## Instalador

- `SetupIconFile`, `WizardImageFile` e `WizardSmallImageFile` apontam para os assets oficiais.
- Atalhos do Menu Iniciar e da Área de Trabalho usam explicitamente o ícone incorporado em `GuiaPlay.exe`.
- O desinstalador mantém nome, publicador e `UninstallDisplayIcon` do GuiaPlay.
- Instalação por usuário, `%LocalAppData%\Programs\GuiaPlay`, `install.json`, updater e preservação de dados permanecem inalterados.
- O banner de 700 × 200 foi preservado como asset oficial futuro; não foi criada página customizada para evitar complexidade e regressões de DPI/acessibilidade.

## Qualidade e distribuição

O pipeline verifica previamente todos os assets obrigatórios e continua responsável por restore, formato, builds/testes Debug e Release, publish self-contained win-x64, ZIP, manifesto, hashes e Setup. A suíte automatizada também verifica referências de projeto, XAML, instalador e script de release sem testes frágeis baseados em pixels.

## Evidências antes da publicação

- builds Debug e Release: 0 warnings e 0 erros;
- testes Debug e Release: 108/108 aprovados;
- publish self-contained, ZIP, manifesto e Setup compilados;
- instalação e desinstalação controladas em pasta isolada, com atalhos apontando para `GuiaPlay.exe` e dados do usuário preservados byte a byte;
- ícone oficial extraído com sucesso do EXE publicado e do Setup;
- nenhum PNG/BMP de branding foi copiado solto para o publish ou ZIP.

As consultas reais `0.5 → 0.6` e `0.6 → 0.6` são executadas após a publicação da prerelease e registradas no fechamento da entrega.
