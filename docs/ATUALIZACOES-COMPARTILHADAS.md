# Manter a base compartilhada e as adaptações de cada instalação

## O que já é possível na 0.15

Receber **código** pelo Git, integrar em uma branch, testar e publicar uma versão adaptada localmente. Não há atualizador automático de executáveis ou da TV, feed de releases, contrato de capacidades ou migração universal de configuração. Dar acesso ao GitHub não atualiza nenhuma instalação.

Recomendação para a fase atual: uma base de referência e branches de integração do destinatário, com adaptação de TV/hardware identificável. Correções genéricas podem voltar como pull request. Nunca trabalhar simultaneamente na `main` para adaptações específicas de uma máquina.

## Duas formas de organizar

| Situação encontrada no destino | Organização |
| --- | --- |
| Mesmo repositório compartilhado | `origin` aponta para `ImCarL6/lazygaming`; `origin/main` é a referência. Trabalho do Henrique em branch própria e PR para mudanças genéricas. |
| Repositório próprio do Henrique | `origin` continua sendo o repositório dele; adicionar `upstream` apontando para `https://github.com/ImCarL6/lazygaming.git`. Não sobrescrever um remoto já existente. |

Inspecionar `git remote -v`, estado e histórico antes de escolher. Usar nome de branch compatível com as instruções locais; exemplos abaixo adotam `codex/`. Configurar usuário/email ou autenticação global não faz parte da migração. O acesso ao repositório privado precisa estar aceito na conta do destinatário.

### Quando existe ancestral comum

Com alterações atuais já preservadas em commits revisados e sem dados privados no índice, criar uma branch de integração **a partir do trabalho atual dele**. Não partir de uma referência limpa que descarte suas mudanças.

```powershell
git status --short
git remote -v
git switch -c codex/integrar-base-0.15
```

No mesmo repositório: `git fetch origin` e selecionar o commit alvo de `origin/main`. No repositório próprio, se `upstream` não existir:

```powershell
git remote add upstream https://github.com/ImCarL6/lazygaming.git
git fetch upstream
```

Antes de qualquer merge, conferir log/diff e `git merge-base HEAD <commit-alvo>`. O alvo de código desta entrega é `dc1b281722db095d52a22bba3be426440f90ec28`; preferir revisão exata a executar sempre o `main` mais novo sem leitura. Com ancestral confirmado, usar `git merge --no-commit --no-ff <commit-alvo>`, resolver cada conflito e testar antes do commit. Se nada estiver em andamento, `git merge --abort` pode não se aplicar; verificar o estado antes de usá-lo.

Não usar escolha global “ours/theirs”, `reset --hard`, `clean -fd` ou force push para resolver integração. Se merge não iniciado ou sem ancestral, seguir o próximo procedimento.

### Quando o projeto começou pelo ZIP 0.11.1

O Git público só começou na 0.15; o ZIP antigo não tem um commit ancestral publicado. **Não forçar merge de históricos sem relação.** Usar comparação de três árvores: A = fontes originais do ZIP 0.11.1; B = projeto atual dele; C = fontes novas.

| Comparação por arquivo | Decisão inicial |
| --- | --- |
| B igual a A e C mudou | C é candidato a entrar, após conferir dependências. |
| B mudou e C igual a A | Preservar B. |
| B e C mudaram | Integração manual de comportamento; sem substituição cega. |
| Arquivo novo/removido/movido | Revisar projetos, imports e testes; exclusão na referência não autoriza apagar uma adaptação. |

O relatório `COMPARACAO-0.11.1-0.15.json` do pacote compara somente A e C. Ele não tem acesso a B. Se A não estiver mais disponível, tentar recuperar o ZIP original; se impossível, registrar a incerteza e revisar B/C manualmente, sem inventar um ancestral.

Se B ainda não usa Git, inicializar **a cópia de trabalho do projeto**, revisar `.gitignore` e registrar um baseline sanitizado antes da integração. Isso não publica dados automaticamente. Depois da primeira migração, documentar a revisão exata de C consumida. Para futuras integrações, comparar a referência anterior consumida, o derivado atual e a nova referência; manter o histórico oficial disponível em clone/remoto separado. Não simular um merge para fazer o Git acreditar que mudanças nunca revisadas foram integradas.

Uma opção futura é transplantar adaptações revisadas para uma branch baseada na referência, em uma cópia separada. Só adotar depois de demonstrar equivalência e preservar o projeto atual/rollback; isso não é requisito para integrar agora.

## Registrar a relação entre base e derivado

Manter no repositório derivado um registro **sem segredos**, por exemplo `BASE-COMPARTILHADA.json`:

```json
{
  "schema": 1,
  "upstream": "https://github.com/ImCarL6/lazygaming",
  "upstreamVersion": "0.15.0",
  "upstreamCommit": "dc1b281722db095d52a22bba3be426440f90ec28",
  "integrationMode": "manual-three-way",
  "downstreamVersion": "preencher-apos-integracao",
  "tvPlatform": "confirmar-no-destino",
  "tvAppRevision": "preencher-sem-identificadores-privados",
  "validatedHostTvPair": false
}
```

É um registro proposto de manutenção: o Host 0.15 **não lê esse arquivo**. Marcar a revisão como consumida só depois de revisar todos os deltas aplicáveis e registrar exceções/itens adiados. Não declarar compatibilidade apenas porque números de versão coincidem.

Configurações de TV física, modo de vídeo, áudio, raízes de jogos, rede e atalhos devem permanecer locais. Correções reutilizáveis entram na base; app/adaptador da TV real fica mantido pelo destinatário até passar revisão/testes para ser compartilhado.

## Ciclo das próximas atualizações

1. Origem publica commit/release com mudanças, testes e requisitos de compatibilidade descritos.
2. Destinatário busca o código; compara desde a revisão consumida. Nenhuma instalação muda nesse passo.
3. Integra em branch, preserva adaptador/configuração e roda testes compartilhados mais os específicos da TV dele.
4. Publica em pasta separada e registra a combinação Host + app TV. Se app antigo passar testes com Host novo, pode ser conservado; se precisar mudança no cliente, preparar ambos antes de instalar.
5. Com nenhuma partida/sessão ativa, atualiza somente os componentes necessários, valida e mantém rollback. Atualizar o registro da base depois do aceite correspondente.

As contribuições do Henrique devem vir com teste do comportamento comum; particularidades de hardware não podem se tornar defaults da origem. Repositório público/privado e permissões não devem mudar por conveniência da integração.

## Próxima evolução do produto — proposta, ainda não implementada

| Ordem | Entrega a desenvolver | Critério para considerar pronta |
| --- | --- | --- |
| 1 | Configuração local validada de destino/mode/áudio/rede/raízes | Mesmo Host nas duas máquinas sem constantes por pessoa; ausência/ambiguidade e rollback cobertos. |
| 2 | Contrato cliente TV e adaptadores explícitos | App existente integrado sem importar APIs de outra TV; testes de ciclo de vida e remoto por plataforma. |
| 3 | Versão de protocolo/capacidades autenticadas e compatibilidade | Cliente antigo tolera campo novo; ação ausente não vira alvo de foco; mudanças incompatíveis têm caminho de migração. |
| 4 | Releases reproduzíveis e matriz Host/TV | Manifesto com revisão, hashes, requisitos, notas e pares testados; nenhum segredo/certificado de pessoa. |
| 5 | Aviso de atualização e instalação assistida no PC | Verifica origem/integridade, espera fim da sessão, prepara backup, migra dados e recupera de falha. |
| 6 | Automatizar atualização quando cada plataforma permitir | Mecanismo de confiança adequado à distribuição e modelo da TV; confirmação de resultado e rollback testados. |

Hashes do ZIP desta entrega comprovam consistência local, não assinatura de release confiável. Não baixar e executar `main` automaticamente, não atualizar durante jogos e não presumir que instalar PC instala também TV. Autostart, serviços e permissões de instalação são decisões locais separadas.

O objetivo final é: melhorias comuns chegam ao derivado com integração pequena e verificável, enquanto o app da TV e a configuração de cada instalação continuam funcionando. Para esta entrega, esse caminho é Git + integração/testes; o atualizador permanece trabalho futuro.
