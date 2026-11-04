# LazyGaming no Windows

Launcher local para jogar pelo controle, com um monitor ou várias telas. A “TV” é a tela de jogo escolhida por você: pode ser uma TV HDMI, monitor DisplayPort ou a própria tela do notebook. Não precisa de app Samsung para jogar por cabo. Não inclui jogos, emuladores, ROMs, saves ou cadastros de outra pessoa.

## Instalar com o LazyGaming-Setup.exe

1. Baixe `LazyGaming-Setup-<versão>.exe` (e, se quiser conferir, o `.sha256` ao lado).
2. Execute. O Windows pode mostrar “O Windows protegeu o computador”, porque o instalador não tem assinatura digital: clique em **Mais informações → Executar assim mesmo** somente se recebeu o arquivo de quem confia.
3. A instalação é só para o seu usuário, sem administrador: programa em `%LOCALAPPDATA%\Programs\LazyGaming`, atalhos **LazyGaming** (área de trabalho e menu Iniciar) e **Central do LazyGaming**. Cada arquivo é conferido por SHA-256 antes de instalar.
4. Ao terminar, a Central abre para a configuração inicial.

Atualizar: saia do LazyGaming pelo ícone da área de notificação e execute o instalador novo; configurações, biblioteca e artes (`%LOCALAPPDATA%\LazyGaming`) são preservadas e a versão anterior fica em `LazyGaming.anterior`. Desinstalar: **Configurações → Aplicativos → LazyGaming** (os dados ficam). Requer Windows 10/11 64 bits e Microsoft Edge WebView2 Runtime (já presente na maioria dos Windows 11).

Nesta versão o launcher do PC não mostra o modo Stream: jogar é pela tela escolhida (cabo). Problemas com o controle ficam registrados em `%LOCALAPPDATA%\LazyGaming\controller-diagnostics.jsonl` (sem dados pessoais); envie esse arquivo se algo parar de responder.

## Configuração inicial automática — PC 0.21

Na primeira abertura, a Central pede apenas tela de jogo, controle quando não for reconhecido, atalho para abrir/ocultar o launcher, áudio e uso das outras telas. Use **Gravar no controle**, solte os botões e faça o gesto desejado (segurar ou duplo toque); duração e gesto também podem ser ajustados depois. Salvar não muda as telas naquele momento. Atualizações preservam escolhas existentes. O padrão é Select+Start por1s; no DualSense com microfone disponível, dois toques no microfone abrem o launcher e segurar por1,5s alterna o mouse por touchpad, com clique físico. **Usar padrão** restaura esses atalhos. **Configuração inicial**, nas configurações, permite rever o assistente.

O modo Automático identifica DualSense pela interface HID e só anuncia capacidades após um relatório válido. Touchpad/clique e atalhos de microfone disponíveis são usados automaticamente, respeitando personalizações. Bluetooth básico continua sem capacidades que o relatório não fornece. Sem controle detectado, escolha o tipo ou deixe a detecção automática para conectar depois.

PC0.21.1 solicita automaticamente os recursos completos quando um DualSense conecta em Bluetooth básico. Se o controle recusar, Create+Options permanece como alternativa para abrir; USB disponibiliza microfone e touchpad. O controle detectado aparece na Central sem recarregar. Em Opções avançadas do controle, é possível desativar futuras solicitações Bluetooth; reconecte o controle para retornar ao modo básico.

Confirmar e voltar seguem os botões inferior/direito no DualSense e Xbox. Nintendo detectado ou escolhido usa A para confirmar e B para voltar automaticamente; a troca manual fica nas opções avançadas. Essas escolhas só afetam o LazyGaming.

Uma tela Samsung é identificada pelo nome/identidade fornecida pelo Windows. Um cartão discreto sugere a integração **se** for TV Tizen compatível; identificar a marca não comprova modelo/sistema. Agora não dispensa o cartão de forma persistente. Não há popup, scan de rede, instalação ou ativação LAN automática. Opções avançadas ficam na Central.

Ao sair do aplicativo, a Central mostra uma tela offline com **Abrir LazyGaming**. O Windows registra um comando local fixo para reabrir a Central; o navegador pode pedir autorização para abrir o aplicativo. Se o registro não estiver disponível, use o atalho da área de trabalho. O endereço recebido pelo protocolo não é executado como comando. A sessão antiga da Central não é reutilizada após reiniciar.

## Primeiro uso

1. Extraia o ZIP para uma pasta fixa e abra `Central-LazyGaming.cmd`. O pacote padrão para Windows x64 inclui os runtimes .NET; o WebView2 Runtime precisa estar instalado no Windows.
2. Em **Configurações**, escolha a tela de jogo e a saída de áudio. Uma tela única funciona automaticamente. Com várias telas e um único HDMI, esse HDMI é a sugestão automática; prefira salvar a tela certa para evitar ambiguidade. A escolha usa a identidade física, não o número do monitor. Se ela sumir, o app solicita correção e não escolhe outra silenciosamente.
3. Escolha se quer manter os outros monitores durante a sessão. Com uma única tela, ambas as escolhas conservam a tela de jogo. Em várias telas, preserva os modos e a disposição relativa dos monitores. Telas clonadas/origens sem espaço podem exigir ajustar o Windows antes; não há garantia para qualquer combinação arbitrária de GPUs, docks ou cabos.
4. Por padrão, ao terminar a sessão, os monitores anteriores voltam e a tela de jogo permanece disponível. Ative **Restaurar também o estado anterior da TV** para recuperar exatamente o estado anterior, inclusive uma TV inicialmente desativada. Uma instalação anterior conserva sua política de restauração até você alterá-la. Isso controla o display no Windows, não a energia física da TV.
5. Em **Pastas e busca**, adicione suas pastas. Steam instalada é descoberta separadamente. Cadastre executáveis diretos/recomps em **Adicionar jogo**. Para Eglantine, selecione o executável e conteúdo NUP/ExeFS. A busca não abre jogos. Outros emuladores ainda precisam de adaptadores próprios; não há interpretação universal de ROMs ou comandos livres.
6. Abra `Abrir-LazyGaming.cmd`. Escolha o jogo e **Cabo**. Trocar/encerrar usa o jogo identificado e pede confirmação; forçar avisa sobre perda de progresso. O painel sozinho não altera telas. A restauração acontece após cinco segundos sem jogo/abertura pendente.

Jogos e versões/idiomas ficam sob uma ficha; os arquivos precisam existir no PC de cada pessoa. O launcher continua dependendo da configuração correta dos controles dentro de cada jogo/emulador. Não copia saves nem configura gráficos universalmente. A tela de jogo usa o modo nativo preferido do Windows, sem impor Samsung ou 4K/60.

## Controles preparados

| Controle | Caminho | Situação |
| --- | --- | --- |
| DualSense | HID compartilhado USB/Bluetooth | Base existente; novos atalhos e analógico ainda exigem teste físico. Bluetooth básico pode omitir microfone/touchpad. |
| Xbox One / Series e compatíveis | XInput do Windows | Adaptador/polling implementados e testes sintéticos; sem validação física nesta revisão. |
| 8BitDo Ultimate | Modo XInput compatível com o modelo | Mesmo adaptador Xbox. No Ultimate Bluetooth original, o fabricante documenta XInput em USB/2,4 GHz; Bluetooth é outro modo. Conferir o modelo exato. |
| Nintendo Switch Pro original | HID completo 0x30, experimental | Leitura preparada, centro nominal dos analógicos; sem inicialização USB/calibração nativa ou teste físico. Não promete funcionar em toda conexão. |
| Joy-Con / outros HID | Ainda não implementado | Se já aparecer como XInput por uma configuração existente no PC, pode usar esse caminho; o pacote não instala drivers ou mapeadores. |

Fontes: [formato Nintendo documentado no driver Linux](https://github.com/torvalds/linux/blob/master/drivers/hid/hid-nintendo.c), [Microsoft XInput](https://learn.microsoft.com/en-us/windows/win32/xinput/getting-started-with-xinput), [8BitDo Ultimate Bluetooth](https://support.8bitdo.com/faq/ultimate-bluetooth-controller.html), [8BitDo Ultimate 2,4 GHz](https://support.8bitdo.com/faq/ultimate-2-4g-controller.html).

D-pad/analógico esquerdo navega; botão inferior confirma e o da direita volta. Pode inverter confirmar/voltar. Padrão de abertura: **Select + Start por 1 s** (View/Menu, Create/Options ou −/+). Padrão de mouse: **clique dos dois analógicos por 1 s**. Os atalhos aceitam combinações e duplo toque ou hold de 0,5–3 s. Devem começar soltos após conectar/alterar configuração. O microfone DualSense conserva seus atalhos, com opção de desativá-los.

Em modo mouse, o analógico direito move o ponteiro com zona morta, curva de precisão e velocidade ajustáveis. Botão superior direito faz clique esquerdo; superior esquerdo faz clique direito, ambos configuráveis. Inclinação pequena move devagar; inclinação maior acelera. Não há clique repetido ao segurar. Touchpad DualSense continua disponível. Ative o mouse com jogo/sessão ou painel em foco; ao terminar/ocultar sem sessão, desconectar ou mudar configuração, o mouse é desativado.

Botões traseiros/macros do 8BitDo não são botões independentes do XInput: configure no software do fabricante para emitir a combinação escolhida. O launcher não grava macros no controle. Não usa botão Xbox/Home, que pode pertencer ao sistema. Em automático, prioriza DualSense, depois primeiro slot XInput e Switch Pro; configure o tipo se houver mais de um. Não há navegação simultânea por vários controles ou seletor por slot nesta revisão.

O painel exige foco e soltura inicial; não instala controle virtual nem captura o gamepad exclusivamente. Jogos que leem input sem foco podem reagir também. Atalhos devem ser escolhidos sem conflito com o jogo.

## Ícone e pausa

O auxiliar aparece na área de notificação do Windows com um L verde (ativo) ou cinza (pausado). O Windows pode colocá-lo nos ícones ocultos; você pode fixá-lo pela própria barra. Duplo clique abre a Central. O menu oferece abrir launcher, Central, pausar/ativar e sair. A Central também permite ativar/pausar.

Pausar desliga a leitura do controle, mouse e busca automática e bloqueia novas aberturas/trocas de telas pelo launcher. Não fecha o jogo atual e mantém a recuperação de telas/áudio ao terminar a sessão; fechamento normal de jogo continua disponível. A escolha de pausa é guardada entre reinicializações e atualizações. Sair/fechar para atualizar exige ausência de jogo, abertura pendente e sessão. Não instala autostart por conta própria.

## Atualizações

Cada ZIP tem versão e `release.json` com arquitetura e SHA-256 dos arquivos. Programas ficam na pasta extraída; biblioteca, atalhos de controle, tela escolhida e demais preferências ficam em `%LOCALAPPDATA%/LazyGaming`. Atualizar os binários não substitui esses dados. Os atalhos devem apontar sempre para a mesma pasta fixa.

1. Receba o novo ZIP de uma origem confiável e extraia para **outra pasta**. O hash detecta corrupção; não é assinatura nem comprova a origem.
2. Encerre o jogo pelo launcher, aguarde a restauração e use **Central → Configurações → Fechar auxiliar para atualizar**.
3. Na pasta do **novo** pacote, execute:

```powershell
powershell -NoProfile -File .\Atualizar-PC.ps1 -InstallationDirectory 'D:\Aplicativos\LazyGaming'
```

O destino é sua instalação anterior. Pode conferir previamente com `-VerifyOnly`. O atualizador recusa mesma versão/downgrade, arquitetura diferente, pacote adulterado, caminhos externos/links, auxiliar em execução e snapshot de sessão pendente. Prepara uma pasta separada, guarda a instalação anterior ao lado e confere a versão do novo executável; falha nessa conferência restaura os binários anteriores. Não encerra jogos/processos, não reinicia o Windows e não apaga dados/saves. Depois, abra o atalho habitual.

A primeira versão desse fluxo é 0.18.0; instalações anteriores sem `release.json` precisam ser preparadas separadamente. Não há busca/download automático, canal de releases hospedado, assinatura digital ou garantia universal de reversão de futuros formatos de dados. Evoluções devem manter leitura dos dados anteriores e fazer backup antes de migrá-los; uma migração futura não pode presumir compatibilidade de downgrade.

## Integração Samsung Tizen opcional

A Central oferece **Permitir conexão do app Tizen pela rede local**. Salve com a sessão encerrada, use Sair e abra o auxiliar novamente. O padrão do pacote é acesso somente neste PC; com a opção ativa ele atende IPv4 privado 10.x, 172.16–31.x e 192.168.x, na sub-rede das interfaces locais. Um atalho com `--lan` continua forçando o acesso LAN. Não expõe na Internet nem instala regra de Firewall automaticamente.

O ZIP inclui `TV-opcional` com fontes/runtime, licenças e empacotador, sem certificado, segredo ou vínculo de outra TV. Requer Samsung Tizen 6+ e APIs compatíveis; a única família fisicamente validada até aqui é a CU7700 do projeto. Cada TV precisa de modo de desenvolvimento e certificado/perfil local autorizado. Na pasta `TV-opcional`, prepare e assine com Tizen Studio:

```powershell
powershell -NoProfile -File scripts/Empacotar-TV.ps1 -StageOnly
powershell -NoProfile -File scripts/Empacotar-TV.ps1 -TizenCli 'C:\tizen-studio\tools\ide\bin\tizen.bat' -SigningProfile 'SEU_PERFIL'
```

Instalar o WGT é uma etapa separada, direcionada à TV correta. Atualizações TV devem manter identidade do app e certificado de autor; não reutilize os certificados privados de outra pessoa. Não há root, firmware, menu de serviço, boot ou configuração global de teclas envolvidos.

Com PC/TV na mesma rede privada e acesso permitido pelo Firewall, gere o código na Central e informe endereço/código no app TV. Novo pareamento substitui o anterior; na mesma TV use a recuperação existente. Pareamento inicial e reserva continuam separados do acesso local PC. A tela HDMI escolhida na Central é o destino de jogo; entrada HDMI física da TV também precisa corresponder ao cabo. Home preserva o retorno. Sem app instalado ou sem APIs compatíveis, permanece possível jogar pelo launcher Windows na TV por HDMI.

O motor Stream continua disponível para setups com Sunshine/Moonlight já preparados, GPU/rede compatíveis e pareamento próprio. Ativar Tizen não instala Sunshine, drivers ou serviços, nem comprova desempenho/streaming. Instalação, navegação, HDMI/áudio e stream precisam de validação física em cada novo modelo.

## Gerar um pacote

```powershell
powershell -NoProfile -File scripts/Distribuir-PC.ps1
```

Gera ZIP Windows x64 com runtimes, licenças, guia, atualizador, fontes correspondentes do LazyGaming e fontes/build do Moonlight Tizen 1.19.0 em uma pasta nova sob `outputs/distribution`. `-FrameworkDependent` cria pacote menor, exigindo .NET Desktop e ASP.NET Core Runtime 10 no destino. WebView2 é requisito separado. Não instala nem publica online. Acesso local é o padrão; `--lan` precisa ser solicitado explicitamente para o app TV. Sem autostart, serviço, Firewall ou configuração de TV automáticos.
