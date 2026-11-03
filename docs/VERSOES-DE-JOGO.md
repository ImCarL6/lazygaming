# Versões de um jogo — 0.17.0

## Uso

Pokopia mantém uma única capa. Ao escolher Jogar ou confirmar a capa, o painel Como jogar apresenta Idioma, com Português (Brasil) e English acima de Cabo/Stream. Não há uma segunda confirmação ou modal de idioma. A seleção atual tem indicação própria e continua marcada ao levar o foco até Cabo/Stream. Setas, OK, Voltar, teclado e mouse usam a navegação existente.

Cancelar não altera a preferência nem os jogos. O PC guarda a última alternativa que conseguiu lançar, usando gravação durável e reserva, independentemente do dispositivo usado. Continuar não abre o seletor nem inicia outra instância. O estado em execução informa o idioma ativo. Trocar idioma exige primeiro encerrar o jogo pela confirmação normal do launcher. Emuladores podem exibir uma confirmação adicional própria; sua configuração deve ser decidida localmente, preservando a confirmação do launcher.

## Exemplo de integração: Pokopia

Uma combinação validada usa Eglantine 2.5.0, jogo 2.0.1 e tradução LucianoNeo v2.5.0 com ícones DualSense na alternativa Português (Brasil); English usa o conteúdo original 2.0.1. As duas alternativas usam o mesmo emulador e perfil portátil. Não há cópia, troca ou aplicação de tradução a cada abertura; apenas muda o conteúdo entregue ao emulador. Configurações, controle e diretório de saves são comuns, sem importar, converter ou sincronizar saves. O jogo deve continuar em English internamente: a tradução do pacote português substitui esses textos. Caminhos, pacotes e perfil pertencem à instalação local, sem distribuição pelo LazyGaming.

O launcher não traduz outros jogos automaticamente. O padrão é independente do nome do jogo, com rótulo e alternativas definidos no cadastro. O adaptador atual suporta conteúdos ExeFS/NUP do Eglantine; argumentos arbitrários, scripts de troca e alteração automática de arquivos não são aceitos. Outros runtimes precisam de um adaptador validado antes de serem cadastrados com alternativas.

## Cadastro e API

Uma ficha local pode acrescentar:

```json
{
  "ChoiceLabel": "Idioma",
  "Choices": [
    { "Id": "pt-br", "Name": "Português (Brasil)", "ContentPath": "D:\\Jogos\\Conteudo\\Traduzido.nup" },
    { "Id": "en", "Name": "English", "ContentPath": "D:\\Jogos\\Conteudo\\Original.nup" }
  ]
}
```

Esses campos complementam Executable, WorkingDirectory, ContentPath e Metadata; não substituem a ficha completa. O cadastro exige de duas a oito alternativas com chaves e caminhos distintos, nomes curtos, rótulo e conteúdo válido. Importação e substituição usam a validação e a gravação existentes, preservando identidade e outros cadastros. A Central aceita a ficha pela importação existente; não oferece ainda um editor visual específico das alternativas.

`GET /api/status` acrescenta `choiceLabel`, `choices: [{id,name,available}]`, `selectedChoice` e `runningChoice` por jogo. Nenhum caminho local é publicado nesses campos. `POST /api/games/{id}/start` aceita `{mode,choice}`; choice é somente a chave cadastrada. Caminho, chave desconhecida ou conteúdo indisponível são recusados antes de preparar telas/áudio. Clientes antigos podem omitir choice e usar a preferência guardada, ou a primeira alternativa. Sem Choices, o comportamento anterior permanece.

As versões são reconhecidas pelo executável e argumento exatos. Foco, fundo, mover para TV, estado, restauração e encerramento consideram todas as alternativas cadastradas e continuam verificando o processo antes de atuar. Outro conteúdo no mesmo emulador não autoriza encerramento. Uma alternativa ausente não remove a outra da biblioteca. A escolha persistida fica em `library/launch-preferences.json`, sem conteúdo ou dados de pareamento no WGT.

## Verificação e limites

Core.Tests, Host.Tests, integração HTTP e regressões JS/Python/PowerShell em `scripts/Testar.ps1`. Cobertura nova: importação/identidade sem duplicação, alternativas inválidas/ausentes, argumento de lançamento, preferência persistida/corrompida, retomada, transporte e encerramento de runtime exato. A prévia visual usa biblioteca e autenticação isoladas; conferida em 720p/1080p/4K, inclusive durante polling e com Stream atrasado. Nenhum jogo real é iniciado pela prévia.

Uma atualização dos conteúdos pode usar `--replace-local-game ID_EXISTENTE ficha.json` com a mesma identidade e as novas alternativas; isso conserva capa, nome e seleção. O cadastro privado não acompanha o código no Git. Verifique os arquivos e a compatibilidade da tradução antes de substituir a ficha e não atualize durante uma partida.

Em validação local, as duas alternativas 2.0.1 abriram via API LAN/Cabo, com janela 3840×2160, foco e encerramento normal/restauração. Teste de uma cópia isolada do save carregou o mundo com tradução e ícones; o save ativo foi preservado. Isso não comprova controle físico Samsung/DualSense, streaming ou estabilidade prolongada. Instalação, abertura observada, uso prolongado e aprovação física são etapas distintas. Relatórios operacionais, caminhos, hashes de saves e capturas permanecem locais.

No Eglantine 2.5.0, `confirmStop=2` e `confirmStop\default=false` no perfil local desativam sua confirmação adicional. O [fonte da versão usada](https://github.com/Djihads80/Eglantine/blob/216b5034c6/src/yuzu/main_window.cpp) reconhece Ask_Never em ConfirmClose; o [enum](https://github.com/Djihads80/Eglantine/blob/216b5034c6/src/common/settings_enums.h) define sua posição. Esse ajuste é opcional, exige decisão e backup locais e não é aplicado automaticamente pelo LazyGaming. O launcher mantém confirmação de fechamento e aviso de perda de progresso ao forçar.
