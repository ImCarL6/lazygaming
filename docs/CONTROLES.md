# Contrato de controles

`IControllerAdapter.Decode(ReadOnlySpan<byte>)` devolve `ControllerState` ou `null` para um relatório inválido. O resultado contém botões semânticos, neutralidade de **todos** os controles, botão de microfone opcional, quadro de toque opcional, pressão física do touchpad e atalho de painel. Neutralidade não pode significar apenas “nenhum botão de menu”: deve incluir gatilhos e ambos os analógicos.

`null` em `Microphone`/`Touch` indica capacidade ausente naquele relatório. O adaptador não deve converter padding em coordenadas ou inventar botão solto. `null` no estado inteiro limpa o processador e impede confirmação de soltura.

## Primeiro adaptador: DualSense

| Formato | Relatório | Recursos |
| --- | --- | --- |
| USB | ID 0x01, 64 bytes | Botões, analógicos/gatilhos, microfone e dois pontos de toque. |
| Bluetooth completo | ID 0x31, pelo menos 78 bytes | Mesmos recursos, offsets próprios. |
| Bluetooth básico | ID 0x01, formato básico, inclusive preenchido até 78 bytes | Navegação e Create+Options; não anuncia microfone/coordenadas. Pressão física é lida quando informada. |

Leitura usa VID Sony 054c / PID DualSense 0ce6, prioriza USB e compartilha o dispositivo com jogos. Desde0.21.1, solicita Bluetooth completo automaticamente após um relatório básico válido, sem instalar driver/controle virtual. Outros modelos/PIDs Sony ainda precisam de adaptador e validação próprios; XInput/Switch Pro estão descritos na revisão PC abaixo.

## Política compartilhada

- Direções repetem após 300 ms e depois a cada 140 ms. Confirmar/voltar não repetem enquanto mantidos.
- Create+Options por 1 s ou dois toques curtos no microfone alternam painel. Toque curto tem no máximo 300 ms; intervalo entre solturas até 400 ms. Hold do microfone não vira duplo toque.
- Microfone por 1,5 s alterna mouse apenas durante sessão. Touchpad produz deltas relativos; troca de identificador ou salto grande não desloca o ponteiro.
- Pressão física gera clique esquerdo; dois dedos com pressão geram direito. Toque leve não clica. Cliques mantidos não repetem.
- Menu depende de foco e soltura. Sessão inativa desabilita mouse antes de avaliar cliques. Reset/reconexão elimina estado anterior.

## Adicionar outro controle

1. Implemente `IControllerAdapter` sem chamadas de plataforma. Use `ControllerButtons.Confirm/Back`, sem trazer nomes específicos do fabricante para a política.
2. Adicione sua descoberta/abertura a uma implementação de `IControllerSource`. A conexão expõe `Input`, `ReportLength` e `Adapter`; o consumidor não deve supor DualSense.
3. Injete a fonte no último parâmetro `source` de `TouchpadMouse` no ponto de composição do Host. A saída de mouse também é injetável com `IMouseOutput`.
4. Teste relatórios válidos, inválidos/truncados, capacidades ausentes, botão mantido, perda de foco, desconexão e neutralidade completa. `tests/Core.Tests` inclui um segundo adaptador artificial como prova do contrato, sem afirmar suporte a outro hardware.
5. Valide fisicamente USB/Bluetooth e convivência com jogos antes de anunciar suporte. Controle sem microfone/touchpad conserva navegação; uma combinação de abertura deve ser explicitamente mapeada pelo adaptador.

O controle Samsung entra pelo evento de teclado do Tizen e usa a mesma navegação da interface. O adaptador HID Windows não é executado na TV.

## PC 0.18: XInput, atalhos e ponteiro

`ControllerState` acrescenta botões físicos normalizados e eixos direitos. `XInputConnection` consulta o primeiro slot conectado a cada 16 ms via XInput 1.4; mantém esse slot até desconectar. `XInputAdapter` usa o layout de 12 bytes de XINPUT_GAMEPAD, sem interpretar relatórios HID como Xbox. `SwitchProAdapter` lê somente relatórios completos 0x30 do Pro original (057e/2009), sem escrever no dispositivo/inicializar USB/calibrar sticks. É experimental e Joy-Con não está implementado.

As fontes podem ser selecionadas na Central. Automático prioriza DualSense, primeiro XInput, Switch Pro. Apenas uma conexão alimenta o processador. Trocar fonte/opções limpa atalhos e mouse; reconexão exige soltura antes de atalhos configurados. Neutralidade inclui ambos os sticks/gatilhos/botões.

`ControllerOptions` configura combinações, hold/duplo toque, cliques e inversão confirmar/voltar. Combinações reservam botões de confirmar/voltar para não iniciar jogos ao chamar menu. Atalho que usa microfone substitui seus gestos legados. `StickPointer` usa velocidade por segundo, zona morta radial, curva e resíduos fracionários; pausa longa é limitada a50ms para evitar saltos. Cliques precisam de soltura após ativar mouse. Sem sessão/painel em foco, inválido ou desconexão desativa mouse. Menu mantém proteção por foco/soltura e não isola jogos que leem input em background.

Matriz e fontes do fabricante: [guia PC](PC-PARA-AMIGOS.md). Xbox/8BitDo/Switch são testes de bytes e estado nesta revisão, sem validação física ou promessa de compatibilidade universal.

## PC 0.21: gravação e padrões

`ShortcutCapture` exige neutralidade inicial por80ms, observa botões suportados durante até15s e reconhece combinação com hold de500–3000ms ou dois toques curtos. Desconexão exige nova soltura; timeout/cancelamento não salvam um gesto. Relatórios da gravação não disparam o launcher ou mouse. Captura exclusiva autenticada pela Central, recusada durante jogo/sessão; o usuário confirma ao salvar.

0.21.1 também reconhece um único toque curto após aguardar400ms por eventual segundo toque, mantendo o gesto escolhido na UI. No relatório básico, bits2–7 do terceiro byte são contador; somente bits0–1 impedem soltura. Regressão cobre todos os64 valores do contador e amostra real `01807C7F7A08000C00000000`.

Sem personalização, DualSense que efetivamente fornece microfone recebe microfone duplo toque para menu e hold1,5s para mouse. Mesmos botões podem ter gestos distintos; a mesma combinação e gesto para as duas ações é recusada. Bluetooth básico mantém fallback sem inventar capacidades. Opções salvas não são sobrescritas; **Usar padrão** permite escolhê-las explicitamente.

No Bluetooth básico, o handshake nativo tem78bytes, cabeçalho31/00/10, flags de efeitos zeradas eCRC32 deA2+74bytes. Acesso compartilhado leitura/escrita com fallback somente leitura; escrita limitada a500ms e tentativas espaçadas30s. Não altera rumble, LEDs, áudio, mute ou gatilhos. Só habilita capacidades após receber relatório completo0x31. Preferência avançada `DualSenseBluetoothFeatures` permite desativar futuras solicitações; para retornar ao modo básico, reconectar o controle. Implementação original compatível com o protocolo descrito pelo [SDL](https://github.com/libsdl-org/SDL/blob/main/src/joystick/hidapi/SDL_hidapi_ps5.c). Não usa SDL como dependência.

`PcSetup.Effective` calcula fallback para botões de microfone ausentes sem gravar sobre o atalho salvo, restaura-o ao receber capacidades completas e preenche bindings não especificados com padrões do dispositivo. Nintendo detectado ou selecionado usa troca de confirmar/voltar automaticamente; outros usam botão inferior. `ConfirmBackOverride` nullable permite escolha explícita avançada, e mudança de fonte volta ao automático. Nenhum binding de jogo é modificado.

## PC 0.22: volume rápido

Botão superior (△ DualSense, Y XInput, X Switch Pro; `PadButtons.North`) gera a tecla de interface `1001` (`NativeNavigation.VolumeKey`), sem repetição e só com o launcher do PC em foco. Se o botão superior fizer parte de um atalho configurado, o volume rápido não é gerado. Com o volume aberto, setas/analógico (incluindo repetição ao segurar) ajustam ±5 no volume mestre da saída padrão do Windows (`/api/volume`); OK, Voltar ou o botão superior fecham; fecha sozinho após 3,5 s sem uso. O chip no topo mostra o nível e aceita clique. Teclado/controle remoto da TV não usam esse código.

## PC 0.23.1: arrastar pelo touchpad

Sons durante o segurar do mouse foram removidos em 0.23.2 (só chime na troca). Touchpad pressionado com um dedo emite `left-down`/`left-up` (arrastar), com zona morta de 24 unidades antes do primeiro movimento; o host solta o botão em qualquer reset do processador.
