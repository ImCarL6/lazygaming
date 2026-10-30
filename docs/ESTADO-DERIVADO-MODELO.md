# Estado da integração — modelo para preencher no destino

Copiar para `ESTADO-HENRIQUE.md` no projeto do destinatário. Não preencher com suposições nem incluir credenciais, certificados privados, IDs de recuperação, IPs desnecessários ou diagnósticos pessoais no Git compartilhado. Este modelo contém campos pendentes; não comprova integração.

## Base e instalação

- Data e responsável: pendente.
- Referência anterior: pacote 0.11.1, ou corrigir conforme inspeção.
- Código novo proposto: 0.15.0 / `dc1b281722db095d52a22bba3be426440f90ec28`.
- Referência efetivamente consumida e exceções: pendente.
- Projeto derivado, branch e commit de integração: pendente.
- Host instalado (versão/revisão): pendente.
- App TV instalado (plataforma/versão/revisão): pendente.
- Relação Git: mesmo repo / origin próprio + upstream / comparação manual / pendente.
- Ancestral comum verificado: sim / não / pendente. Evidência: pendente.
- Backup e ponto de retorno locais verificados: pendente.

## Inventário útil

TV/sistema real, framework do app, remoto e ciclo de vida: pendente.
Windows/runtimes, controle e modo USB/BT: pendente.
Destino HDMI/modo suportado, áudio e seleção local: pendente.
Raízes Steam/não Steam e restrições de rede: pendente.
O que funcionava antes e precisa ser preservado: pendente.

## Matriz de integração

| Área | Alteração própria existente | Melhoria da referência | Decisão e evidência |
| --- | --- | --- | --- |
| Core/controle/neutralidade | Pendente | Separação 0.15/reset | Pendente |
| Host/processos/janelas | Pendente | Encerrar/mover/perfis | Pendente |
| Rede/cliente API/recuperação | Pendente | Contratos da referência | Pendente |
| TV/adaptador/armazenamento | Pendente | Integração com Host | Pendente |
| UI/foco/metadados/sons | Pendente | Fluxos 0.12–0.14 | Pendente |
| Telas/áudio/configuração local | Pendente | Preservar validação/rollback | Pendente |
| Build/publicação/atualizações | Pendente | Scripts/Git/CI | Pendente |

## Validação, separada por etapa

| Etapa | Estado | Revisão exata / evidência / limite |
| --- | --- | --- |
| Build | Não executado | Pendente |
| Testes isolados compartilhados | Não executado | Pendente |
| Testes específicos do app TV | Não executado | Pendente |
| Pacote PC preparado | Não preparado | Pendente |
| App TV preparado e assinado localmente | Não preparado | Pendente |
| Host instalado | Não atualizado | Pendente |
| App TV instalado | Não atualizado | Pendente |
| Vínculo próprio preservado | Não verificado | Pendente |
| Remoto/foco/metadados/confirmações | Não testado fisicamente | Pendente |
| Jogo leve/HDMI/som/mover/retornar | Não testado fisicamente | Pendente |
| Encerramento/restauração/falhas | Não testado fisicamente | Pendente |
| Rollback | Não verificado | Pendente |

## Continuidade

- Falhas reproduzidas e hipóteses ainda não confirmadas: pendente.
- Funções adiadas ou não suportadas na TV real: pendente.
- Par Host/TV aprovado e por quem: pendente.
- Próxima revisão a buscar e como comparar desde a base consumida: pendente.
- Mudanças genéricas candidatas a PR e adaptações que ficam locais: pendente.
- Atualização automática: não implementada nesta entrega.
