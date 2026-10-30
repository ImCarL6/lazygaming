# Migração da base 0.11.1 para 0.15.0

Este guia compara a entrega HDMI 0.11.1 com as fontes 0.15.0 do repositório. Ele **não descreve o estado atual do projeto do Henrique**, que deve ser inspecionado no destino. A revisão de código de referência é `dc1b281722db095d52a22bba3be426440f90ec28`.

A 0.15.0 passou build e testes automatizados na origem. A reorganização ainda não recebeu aceite físico; a instalação de referência permanece 0.14.0. O pacote de migração contém fontes e documentação, sem executável para substituir a instalação do destinatário e sem app pronto para sua TV.

## O que mudou desde a entrega antiga

| Área | Melhoria incluída na referência 0.15 | Como aproveitar no projeto derivado |
| --- | --- | --- |
| Interface | Carrossel centralizado PC/TV, ações inferiores, relógio, foco amarelo e sons sintetizados | Portar comportamento e navegação; preservar identidade e adaptações visuais já feitas no destino. |
| Metadados | Cima revela aba; novo Cima ou OK abre; direção mantida não atravessa etapas; volta por camada | Integrar `app.js`, `index.html` e `style.css` em conjunto, adaptando eventos do remoto da TV real. |
| Encerrar | Ação vermelha para o jogo em execução, mesmo com outra capa selecionada; confirmação com Cancelar como opção inicial | Reconsultar estado e conservar identificação do processo. Forçar exige confirmação adicional de perda de progresso. |
| Destino da janela | `Mover para a TV` para jogo já aberto, sem relançar/fechar | Integrar backend, endpoint e UI; validar janela/processo e TV configurada no destino. Tela cheia exclusiva pode não aceitar. |
| Jogos Unity | Perfil de apresentação para jogos identificados por `UnityPlayer.dll` e pasta `_Data`; restauração de opções | Revisar `WindowedOptionsProfile.cs` e `GameDisplaySettings.cs` com seus testes; não aplicar indiscriminadamente a wrappers. Não é correção universal de monitor. |
| Controles | `LazyGaming.Core` portátil e `LazyGaming.Platform.Windows`, fonte e saída de mouse injetáveis | Migrar projetos/referências e composição de `TouchpadMouse` juntos; preservar eventuais adaptadores do destinatário. |
| Ciclo do controle | Reset na reconexão/relatório inválido e bloqueio do último clique após fim da sessão | Portar política com testes de neutralidade, perda de foco, desconexão e capacidade ausente. |
| Plataforma da TV | `tv/platform.js` concentra parte dos efeitos Tizen/PC | Usar como ponto inicial do adaptador da TV real; ainda existem dependências de plataforma em outros arquivos. |
| Desenvolvimento | Solução, build/testes, auditoria Git, publicação PC e staging/assinatura Tizen parametrizados, CI Windows | Integrar sem transportar caminhos, assinatura ou instalação da origem. |

As correções de abertura Steam direta, expiração da abertura e cursor já integravam a entrega 0.11.1. Recuperação de vínculo, proteção por processo, sessão/telas/áudio e fundo PC também não devem ser apresentados como novidades exclusivas da 0.15; devem continuar funcionando após a migração.

Autostart foi acrescentado na instalação da origem depois do pacote antigo. O script acompanha as fontes, mas isso não autoriza nem configura inicialização automática no PC do destinatário. Stream continua opcional e fora do objetivo HDMI do Henrique; não instalar Sunshine/Moonlight para concluir esta migração.

## Antes de integrar

1. Descobrir a pasta e o repositório do projeto já desenvolvido, o auxiliar instalado e o app da TV. Ler as instruções locais antes de modificar. Identificar TV/sistema/versão, framework do app, controle e transporte que realmente existem.
2. Registrar versão instalada e revisão de fontes **separadamente**; listar o que já funciona, alterações sem commit e adaptações próprias. Fazer backup local verificável, inclusive do projeto TV e de seus dados persistidos. Segredos ficam somente no destino, fora de relatórios compartilhados.
3. Criar branch de integração e um ponto de retorno. Conferir o diff antes de registrar arquivos: backup não é autorização para colocar certificados ou dados privados no Git.
4. Comparar três estados: pacote original 0.11.1, projeto atual do destinatário e referência 0.15. Uma lista de diferenças entre os dois pacotes não identifica o que Henrique alterou.
5. Não executar instaladores, auxiliar real, comandos de diagnóstico que alteram telas ou código de jogos durante a comparação. Build e testes isolados vêm primeiro.

## Ordem sugerida de integração

| Etapa | Trabalho | Evidência de conclusão |
| --- | --- | --- |
| 1 | Inventário e base de comparação, sem apagar alterações atuais | Relatório local e backup; histórico/ancestralidade conhecidos. |
| 2 | Core, Platform.Windows, solução e composição do Host | Build e `Core.Tests`; adaptadores próprios preservados. |
| 3 | Correções Host: processos, posicionamento e configurações de jogos | `Host.Tests`, integração HTTP e cenários negativos. |
| 4 | Cliente de API e adaptador do app TV existente | Contratos em [INTEGRACAO-TV.md](INTEGRACAO-TV.md); autenticação e armazenamento preservados. |
| 5 | UI/navegação/metadados/encerramento | Regressões de UI e teste do remoto; só funcionalidades disponíveis entram na navegação. |
| 6 | Publicação em pasta nova e teste conjunto PC/TV | Matriz abaixo; nenhuma troca do executável em uso sem conferir sessão. |
| 7 | Registrar base e adaptações para próximas atualizações | [ATUALIZACOES-COMPARTILHADAS.md](ATUALIZACOES-COMPARTILHADAS.md) e estado local preenchido. |

Evitar substituição integral de `src/` ou `tv/`. Se o app dele já possui cliente de API, rotas, permissões e ciclo de vida próprios, adaptar esse cliente ao Host atualizado é um caminho válido. Usar a UI de referência não é pré-requisito para receber melhorias do backend.

## Dependências ainda específicas da referência

**A 0.15 não tornou o Host universal.** Reconciliar adaptações locais nestes pontos antes de usar hardware real:

| Fonte | Dependência observada | Integração necessária |
| --- | --- | --- |
| `Displays.cs`, `Presentation.cs` | Busca por HDMI/SAMSUNG/3840×2160 | Seleção física configurada, ausência/ambiguidade recusadas. TV conectada e inativa deve continuar válida. |
| `DisplayProfiles.cs` e preparação/restauração de telas | Validações e planejamento de perfil 4K/60 | Parametrizar seleção, modo suportado, planejamento, confirmação, no-op e rollback juntos. |
| `Audio.cs`, `Session.cs` | Prefixos SAMSUNG e SAMSUNG (NVIDIA | Endpoint real por configuração; preservar áudio anterior, retry e override manual da sessão. |
| `Program.cs`, `tv/app.js`, script de autostart | Rede/descoberta focadas em 192.168.*; porta padrão 47831 | Adaptar interfaces/sub-redes do destino em servidor e cliente, sem aceitar Internet/VPN indiscriminadamente. |
| `GameLibrary.cs` | Raiz não Steam padrão `C:\Joguetes` | Preservar raízes descobertas/configuradas localmente. Não copiar catálogo/cache da origem. |
| `GameDisplaySettings.cs` | Nome SAMSUNG e tratamento específico de alguns jogos | Conservar suas correções locais; conferir arquivo modificado e arquivo restaurado. |
| `tv/platform.js`, `presentation.js`, `app.js`, `stream.js`, `config.xml` | Tizen, compositor, viewer, teclas e empacotamento | Adaptar ao app TV já existente. Não instalar WGT em outra plataforma. |

Configuração local persistida e contratos pequenos são a direção desejada, não recursos universais prontos nesta versão. Não substituir as validações por “primeiro monitor/primeiro áudio disponível”.

## Validação mínima antes de substituir a instalação

Executar os comandos de [DESENVOLVIMENTO.md](DESENVOLVIMENTO.md) na cópia integrada. O script padrão de publicação espera o runtime do motor de Stream mesmo num uso HDMI: obter esses arquivos não instala serviço, mas também não configura streaming. Se a variante HDMI remove esse conteúdo, adaptar conscientemente empacotamento e testes; não apenas suprimir uma falha de build.

| Camada | Conferir |
| --- | --- |
| Isolada | Core.Tests, Host.Tests, testes JS/Python relevantes; assinatura adulterada/repetida/expirada recusada; nenhuma chamada a jogos reais. |
| App TV existente | Abrir/voltar/suspender/reabrir, remoto, rede retomada e armazenamento do vínculo; sem retorno acidental à tela inicial nem perda do app próprio. |
| UI | Capas/metadados/aba, Cancelar inicial, alvo do jogo em execução, mover/continuar e ações indisponíveis fora do foco. |
| PC e controle | Foco, soltura, USB/BT suportados, reconexão e mouse só durante sessão; ausência de microfone/touchpad tratada. |
| HDMI físico | Com Henrique ciente e sem partida aberta: jogo leve, áudio, TV como destino, mover janela se suportado, encerramento normal e restauração de monitores/principal/áudio. |
| Falhas | Steam não abre, jogo fecha durante comando, TV ausente, monitor ambíguo, conexão interrompida; erro recuperável sem encerrar processos genéricos. |
| Rollback | Binários e app anteriores recuperáveis; configuração/snapshot compatíveis. Se formatos mudarem, rollback inclui dados próprios previamente salvos. |

Não encerrar Steam, launchers ou `dotnet` genéricos; confirmar processo real e argumento do runtime. Atualização não roda no meio da partida. Preservar identidade/certificado/origem do app e vínculo do próprio Henrique; uma mudança de armazenamento exige migração explícita, não novo pareamento silencioso.

## Referências de código para revisão

- Arquitetura/controle: `src/LazyGaming.Core/`, `src/LazyGaming.Platform.Windows/`, `TouchpadMouse.cs`, `tests/Core.Tests/`, `tests/platform.mjs`.
- Jogo/janela: `Games.cs`, `GameWindowTarget.cs`, `WindowedOptionsProfile.cs`, `tests/Host.Tests/GameMoveTests.cs`.
- UI: `tv/app.js`, `tv/index.html`, `tv/style.css`; `tests/navigation.mjs`, `panel-navigation.mjs`, `controller-focus.mjs`, `cursor.mjs`.
- Rede/vínculo: `Program.cs`, `Security.cs`, `PairingRecovery.cs`, `tv/crypto.js`; `tests/integration.mjs`, `pairing-recovery.mjs`, `recovery-ui.mjs`.

Mudanças locais em DLLs/configurações de um jogo específico da origem não fazem parte deste pacote e não devem ser reproduzidas no equipamento de outra pessoa.
