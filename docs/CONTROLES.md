# Contrato de controles

`IControllerAdapter.Decode(ReadOnlySpan<byte>)` devolve `ControllerState` ou `null` para um relatório inválido. O resultado contém botões semânticos, neutralidade de **todos** os controles, botão de microfone opcional, quadro de toque opcional, pressão física do touchpad e atalho de painel. Neutralidade não pode significar apenas “nenhum botão de menu”: deve incluir gatilhos e ambos os analógicos.

`null` em `Microphone`/`Touch` indica capacidade ausente naquele relatório. O adaptador não deve converter padding em coordenadas ou inventar botão solto. `null` no estado inteiro limpa o processador e impede confirmação de soltura.

## Primeiro adaptador: DualSense

| Formato | Relatório | Recursos |
| --- | --- | --- |
| USB | ID 0x01, 64 bytes | Botões, analógicos/gatilhos, microfone e dois pontos de toque. |
| Bluetooth completo | ID 0x31, pelo menos 78 bytes | Mesmos recursos, offsets próprios. |
| Bluetooth básico | ID 0x01, formato básico, inclusive preenchido até 78 bytes | Navegação e Create+Options; não anuncia microfone/coordenadas. Pressão física é lida quando informada. |

Leitura usa VID Sony 054c / PID DualSense 0ce6, prioriza USB e compartilha o dispositivo com jogos. Não força Bluetooth completo nem instala driver/controle virtual. Outros modelos/PIDs ainda precisam de adaptador e validação próprios.

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
