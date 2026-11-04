# LazyGaming

Launcher para escolher, abrir, continuar e encerrar jogos do PC pela TV Samsung ou por uma janela executável no Windows. Biblioteca e interface compartilhadas; DualSense conectado ao PC. HDMI é o caminho principal, com streaming Moonlight integrado como alternativa.

## Estado — PC 0.21.1 / ZIP distribuível anterior 0.19.0 / TV 0.17.0

A base 0.14.0 já foi usada no PC e em uma Samsung CU7700. A revisão 0.15 organiza o produto em módulos e adiciona testes de adaptadores de controle; 0.15.1 acrescenta fichas locais independentes da Steam; 0.15.2 abre o seletor Cabo/Stream imediatamente e permite escolher Cabo sem aguardar a consulta de Stream. Build/testes automatizados não substituem uso físico: estas revisões ainda precisam ser validadas com DualSense real e TV. Não é uma publicação na loja Samsung nem um instalador universal.

A revisão 0.16 acrescenta a Central do PC para administrar jogos, pastas de busca automática, cadastros locais, monitores e áudio. O cadastro tem gravação durável, reserva e exclusão mútua entre importações; instalações indisponíveis continuam identificáveis na Central.

A revisão 0.17 permite várias versões sob uma única capa. No Pokopia, **Português (Brasil)** e **English** aparecem no próprio painel de Cabo/Stream; a versão em português usa os ícones DualSense. A última versão aberta fica guardada no PC e vale para PC/TV. Continuar retoma a versão em execução; para mudar, encerre pelo launcher e escolha o outro idioma ao jogar novamente. Jogos sem alternativas mantêm o fluxo anterior. [Cadastro de versões e contrato](docs/VERSOES-DE-JOGO.md).

A revisão PC 0.18 prepara distribuição Windows para uma ou várias telas, com destino físico configurável, política opcional de restauração da TV, XInput (Xbox/8BitDo), leitura experimental Switch Pro, atalhos configuráveis e mouse pelo analógico direito. Novas opções em **Central → Configurações**. Pacotes versionados incluem atualizador manual com integridade, backup e rollback dos binários; dados ficam fora da instalação. Testes sintéticos não comprovam os novos controles fisicamente. [Uso, compatibilidade e atualizações](docs/PC-PARA-AMIGOS.md).

A revisão PC 0.19 acrescenta configuração inicial básica, atalho de abertura já no primeiro uso, reconhecimento das capacidades DualSense e sugestão discreta para telas Samsung. Atualizações preservam configurações existentes.

A revisão PC 0.21 simplifica a configuração inicial, grava atalhos diretamente pelo controle e usa microfone com duplo toque para o launcher e hold de 1,5 s para o mouse quando o DualSense fornece esses recursos. O tempo aparece somente para o gesto de segurar. A Central usa tema escuro lavanda/azul e oferece reabertura do aplicativo após sair, por um protocolo local registrado no Windows, com fallback para o atalho. Configurações personalizadas são preservadas. Pacote local self-contained publicado e instalado; novos controles ainda dependem de validação física.

PC 0.21.1 corrige a soltura no Bluetooth básico e solicita os recursos completos do DualSense automaticamente, com fallback para leitura básica. A Central atualiza o controle detectado sem recarregar, aceita gravar um botão com toque simples e mostra a tela offline no topo ao sair. Nintendo detectado ou selecionado usa A para confirmar; a troca manual fica nas opções avançadas. Demais controles confirmam pelo botão inferior. Abertura física do launcher pelo microfone confirmada nesta instalação; demais controles dependem de validação física.

## Usar no PC

Requisitos: Windows 10/11, .NET 10 Desktop Runtime e ASP.NET Core Runtime, WebView2 Runtime, rede local confiável e jogos instalados. Para compilar: SDK .NET 10, Node.js 22+, Python 3.12+ e PowerShell.

```powershell
python -m pip install -r requirements-dev.txt
python scripts/Obter-Motor.py
powershell -NoProfile -File scripts/Testar.ps1
powershell -NoProfile -File scripts/Publicar-PC.ps1
```

Instalador para compartilhar (ZIP de distribuição + `LazyGaming-Setup-<versão>.exe` via IExpress do Windows, por usuário, sem assinatura digital):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Construir-Instalador-PC.ps1
```

O executável fica em `outputs/pc-package/LazyGaming.Host.exe`. Execute com `--lan` para disponibilizar o auxiliar na rede local. Com o auxiliar aberto, `--open-launcher` abre a interface nativa e reutiliza a instância. Os scripts não instalam serviço, tarefa automática, regra de Firewall nem alteram uma instalação existente. Se necessário, permita o executável somente na sua rede privada pelas configurações do Windows.

O pacote é dependente dos runtimes do Windows listados acima. Para uso local existente, os atalhos `Iniciar-LazyGaming.cmd` e `Abrir-LazyGaming.cmd` usam `dist/LazyGaming`; publicar em outra pasta não atualiza esses atalhos automaticamente.

Abra `Central-LazyGaming.cmd`, execute o auxiliar com `--open-hub` ou use **Configurações → Gerenciar biblioteca e pastas** no launcher PC. A Central abre no navegador do PC, com sessão local independente do controle e do vínculo da TV. O acesso deve ser aberto novamente pelo atalho após reiniciar o auxiliar; não exige novo pareamento.

Em **Pastas e busca**, adicione pastas locais, ative ou pause a descoberta Steam e escolha busca automática a cada 1, 5, 15 ou 30 minutos. A rotina procura na pasta escolhida e nas instalações filhas, com deduplicação por instalação/processo; durante um jogo ou abertura pendente, aguarda outra oportunidade. A busca não executa jogos. Pastas indisponíveis e exclusões aparecem no resultado da busca. **Gerenciar** permite conferir arquivos, renomear, ocultar/reexibir e corrigir o caminho de um cadastro local, preservando identidade, ficha e artes. **Adicionar jogo** aceita executável direto, conteúdo Eglantine suportado e imagens locais opcionais, ou uma ficha JSON existente.

Preferências ficam em `library/settings.json`, separadas do catálogo e dos jogos. Cadastro e preferências mantêm reserva `.bak`; uma leitura inválida pode recuperar a última gravação confirmada. Remover uma pasta da busca ou ocultar um jogo preserva seus arquivos e saves. Alterar a busca exige que não haja jogo aberto.

## Usar na TV

App web Tizen 6.0+; validação física anterior em Tizen 9.0. Mantenha PC e TV na mesma LAN. Instale Tizen Studio/TV Extension e configure **localmente** um perfil de assinatura autorizado para sua TV.

```powershell
python scripts/Obter-Motor.py
powershell -NoProfile -File scripts/Empacotar-TV.ps1 -StageOnly
powershell -NoProfile -File scripts/Empacotar-TV.ps1 -TizenCli 'C:\tizen-studio\tools\ide\bin\tizen.bat' -SigningProfile 'SEU_PERFIL_LOCAL'
python scripts/Verificar-Pacote.py
```

`StageOnly` prepara arquivos sem assinatura. A CLI gera `outputs/LazyGaming.wgt`; instalação na TV é uma etapa separada pela CLI Tizen. Atualizações de um app existente devem conservar identidade de aplicação e certificado de autor. Não substitua o certificado de uma instalação já pareada.

Para preservar um WGT anterior, use `-OutputDirectory outputs/tizen-0.15.0` no empacotador e `--package outputs/tizen-0.15.0/LazyGaming.wgt` no verificador.

No primeiro uso, abra `http://127.0.0.1:47831` no PC, gere o código e conecte a TV. Novo pareamento substitui a chave anterior; use **Recuperar conexão** na própria TV para vínculos existentes. O painel PC utiliza autenticação local independente.

## Controles

- Samsung: setas, OK e Voltar; Home continua disponível.
- DualSense: D-pad/analógico esquerdo navega; X confirma; círculo volta uma camada.
- Create + Options por 1 s abre/oculta o painel PC. Duplo toque no microfone também funciona quando o relatório USB/Bluetooth fornece esse botão.
- Durante uma sessão, microfone por 1,5 s alterna mouse. Deslizar o touchpad move; pressionar faz clique esquerdo; pressionar com dois dedos faz clique direito. Toque leve não clica.
- Bluetooth básico pode omitir microfone/coordenadas. Não há controle virtual ou modo exclusivo. O painel exige foco e soltura dos botões; jogos que recebem input em segundo plano ainda podem reagir.

Monitores começam em **Ligados** quando não há escolha anterior. O auxiliar guarda a última escolha, inclusive **Desligados**, para as próximas sessões e inicializações do Windows, com gravação atômica e cópia de reserva. Guardar essa escolha sem jogo não altera as telas naquele momento.

## Arquitetura e desenvolvimento

| Módulo | Responsabilidade |
| --- | --- |
| `src/LazyGaming.Core` | Contratos e estado normalizado dos controles, DualSense, gestos, atalhos, repetição e soltura; sem chamadas ao Windows. |
| `src/LazyGaming.Platform.Windows` | HID compartilhado e saída de mouse Windows; depende apenas do núcleo. |
| `src/LazyGaming.Host` | Executável Windows, API autenticada, biblioteca, sessão, telas/áudio e WebView2/DWM. |
| `tv` | Uma interface usada pelo WGT Tizen e pelo executável PC. `platform.js` e `presentation.js` isolam efeitos de plataforma; política de stream separada do motor. |
| `tests` | Núcleo portátil, integração do auxiliar simulado e regressões da UI. |

[Arquitetura](docs/ARQUITETURA.md) · [Adaptadores de controle](docs/CONTROLES.md) · [Desenvolvimento e validação](docs/DESENVOLVIMENTO.md)

Para integrar a base a um projeto já adaptado, use o [guia de migração 0.11.1 → 0.15](docs/MIGRACAO-0.15.md), o [contrato de integração do app TV](docs/INTEGRACAO-TV.md) e o [fluxo de atualizações compartilhadas](docs/ATUALIZACOES-COMPARTILHADAS.md). Há um [prompt para a IA do Henrique](docs/PROMPT-HENRIQUE-0.15.md) e um [modelo de estado da integração](docs/ESTADO-DERIVADO-MODELO.md). O fluxo atual recebe código por Git e exige integração/testes; não instala atualizações automaticamente.

## Dados e distribuição

Jogos locais fora da raiz descoberta podem ser cadastrados sem executar o jogo:

```powershell
.\LazyGaming.Host.exe --import-local-game 'C:\Fichas\aventura.json'
```

O cadastro aceita um executável direto, dentro do diretório de trabalho, sem argumentos. O arquivo JSON contém caminhos da instalação e uma ficha visual independente:

Para uma instalação Eglantine existente, a ficha também pode informar `ContentPath` com o caminho absoluto do ExeFS `main`, acompanhado de `main.npdm`, ou de um contêiner `.nup`. Para NUP, o cadastro confere cabeçalho, limites/tamanho e hash da tabela de arquivos, sem extrair o jogo. Esse modo inicia o conteúdo em tela cheia (`-f -g`) sem Shell e identifica o processo pelo caminho exato do conteúdo. Argumentos arbitrários continuam recusados; o cadastro não inclui arquivos do jogo ou do emulador.

Ao trocar a pasta de uma instalação local já cadastrada, `--replace-local-game ID_EXISTENTE ficha.json` mantém a identidade, capa, nome personalizado e seleção. A substituição exige um único cadastro anterior do mesmo título e recusa conflito com outra instalação. O identificador fica apenas no cadastro interno; não é mostrado na biblioteca. Reimportar a nova instalação também conserva essa identidade. A operação não remove executáveis ou saves das versões antigas.

```json
{
  "Executable": "D:\\Jogos\\Aventura\\aventura.exe",
  "WorkingDirectory": "D:\\Jogos\\Aventura",
  "Metadata": {
    "StoreId": null,
    "Source": "local",
    "Title": "Minha aventura",
    "Year": 2023,
    "Developers": [],
    "Genres": ["Aventura"],
    "SourceUrl": "https://example.com/jogo",
    "Description": "Descrição do jogo em português.",
    "Language": "Textos em português brasileiro",
    "Artwork": {
      "Cover": "capa.jpg",
      "Backdrop": "fundo.jpg",
      "CoverSourceUrl": "https://example.com/jogo/capa.jpg",
      "BackdropSourceUrl": "https://example.com/jogo/fundo.jpg"
    }
  }
}
```

As imagens são relativas à pasta da ficha ou caminhos absolutos. O importador copia somente as artes para o armazenamento privado, reduzindo a capa até 480×720 e o fundo até 1600×900, sem ampliação; o ícone vem do executável. Guarda cadastro em `library/local-games.json`, originais reduzidos em `library/imports` e artes servidas em `library/art`, com atribuição. Nenhum arquivo do jogo é copiado ou alterado. Reimportar a mesma instalação atualiza a ficha sem duplicar; o nome personalizado permanece em `library/catalog.json`. As fichas Steam existentes continuam compatíveis.

Depois de importar pela CLI, use **Configurações → Buscar jogos instalados**, aguarde a busca automática ou reinicie o auxiliar, com os jogos fechados. A Central atualiza a biblioteca imediatamente ao salvar. PC/TV recebem a mesma ficha e artes pelo auxiliar; a UI exibe descrição/idioma e permite personalizar o nome. Fichas locais não são associadas à busca Steam. A importação não instala o auxiliar nem atualiza o app da TV. `LAZYGAMING_IMPORT_DATA` permite importar para uma pasta isolada de teste.

Pareamento, recuperação, snapshots e cache ficam em `%LOCALAPPDATA%/LazyGaming`, fora do Git/WGT. Nunca exponha a API à Internet; ela usa HTTP/HMAC em LAN confiável. Encerrar pede confirmação; forçar exige confirmar possível perda de progresso. Telas/áudio são restaurados ao terminar a sessão.

O runtime Moonlight compilado é baixado separadamente da versão oficial 1.19.0 e conferido com SHA-256; não entra no Git. `Obter-Motor.py --wgt caminho/Moonlight.wgt` permite usar uma cópia oficial local. Código GPL-3.0; avisos e condições dos componentes em [THIRD_PARTY.md](THIRD_PARTY.md) e [LICENSE](LICENSE). O CI verifica build/testes e prepara o executável sem instalar ou publicar pacotes binários.
