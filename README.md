# LazyGaming

Launcher para escolher, abrir, continuar e encerrar jogos do PC pela TV Samsung ou por uma janela executável no Windows. Biblioteca e interface compartilhadas; DualSense conectado ao PC. HDMI é o caminho principal, com streaming Moonlight integrado como alternativa.

## Estado — fontes 0.15.0

A base 0.14.0 já foi usada no PC e em uma Samsung CU7700. Esta revisão organiza o produto em módulos e adiciona testes de adaptadores de controle. Build/testes automatizados não substituem uso físico: a reorganização ainda precisa ser validada com DualSense real e TV antes de substituir a instalação existente. Não é uma publicação na loja Samsung nem um instalador universal.

## Usar no PC

Requisitos: Windows 10/11, .NET 10 Desktop Runtime e ASP.NET Core Runtime, WebView2 Runtime, rede local confiável e jogos instalados. Para compilar: SDK .NET 10, Node.js 22+, Python 3.12+ e PowerShell.

```powershell
python -m pip install -r requirements-dev.txt
python scripts/Obter-Motor.py
powershell -NoProfile -File scripts/Testar.ps1
powershell -NoProfile -File scripts/Publicar-PC.ps1
```

O executável fica em `outputs/pc-package/LazyGaming.Host.exe`. Execute com `--lan` para disponibilizar o auxiliar na rede local. Com o auxiliar aberto, `--open-launcher` abre a interface nativa e reutiliza a instância. Os scripts não instalam serviço, tarefa automática, regra de Firewall nem alteram uma instalação existente. Se necessário, permita o executável somente na sua rede privada pelas configurações do Windows.

O pacote é dependente dos runtimes do Windows listados acima. Para uso local existente, os atalhos `Iniciar-LazyGaming.cmd` e `Abrir-LazyGaming.cmd` usam `dist/LazyGaming`; publicar em outra pasta não atualiza esses atalhos automaticamente.

## Usar na TV

App web Tizen 6.0+; validação física anterior em Tizen 9.0. Mantenha PC e TV na mesma LAN. Instale Tizen Studio/TV Extension e configure **localmente** um perfil de assinatura autorizado para sua TV.

```powershell
python scripts/Obter-Motor.py
powershell -NoProfile -File scripts/Empacotar-TV.ps1 -StageOnly
powershell -NoProfile -File scripts/Empacotar-TV.ps1 -TizenCli 'C:\tizen-studio\tools\ide\bin\tizen.bat' -SigningProfile 'SEU_PERFIL_LOCAL'
python scripts/Verificar-Pacote.py
```

`StageOnly` prepara arquivos sem assinatura. A CLI gera `outputs/LazyGaming.wgt`; instalação na TV é uma etapa separada pela CLI Tizen. Atualizações de um app existente devem conservar identidade de aplicação e certificado de autor. Não substitua o certificado de uma instalação já pareada.

No primeiro uso, abra `http://127.0.0.1:47831` no PC, gere o código e conecte a TV. Novo pareamento substitui a chave anterior; use **Recuperar conexão** na própria TV para vínculos existentes. O painel PC utiliza autenticação local independente.

## Controles

- Samsung: setas, OK e Voltar; Home continua disponível.
- DualSense: D-pad/analógico esquerdo navega; X confirma; círculo volta uma camada.
- Create + Options por 1 s abre/oculta o painel PC. Duplo toque no microfone também funciona quando o relatório USB/Bluetooth fornece esse botão.
- Durante uma sessão, microfone por 1,5 s alterna mouse. Deslizar o touchpad move; pressionar faz clique esquerdo; pressionar com dois dedos faz clique direito. Toque leve não clica.
- Bluetooth básico pode omitir microfone/coordenadas. Não há controle virtual ou modo exclusivo. O painel exige foco e soltura dos botões; jogos que recebem input em segundo plano ainda podem reagir.

## Arquitetura e desenvolvimento

| Módulo | Responsabilidade |
| --- | --- |
| `src/LazyGaming.Core` | Contratos e estado normalizado dos controles, DualSense, gestos, atalhos, repetição e soltura; sem chamadas ao Windows. |
| `src/LazyGaming.Platform.Windows` | HID compartilhado e saída de mouse Windows; depende apenas do núcleo. |
| `src/LazyGaming.Host` | Executável Windows, API autenticada, biblioteca, sessão, telas/áudio e WebView2/DWM. |
| `tv` | Uma interface usada pelo WGT Tizen e pelo executável PC. `platform.js` e `presentation.js` isolam efeitos de plataforma; política de stream separada do motor. |
| `tests` | Núcleo portátil, integração do auxiliar simulado e regressões da UI. |

[Arquitetura](docs/ARQUITETURA.md) · [Adaptadores de controle](docs/CONTROLES.md) · [Desenvolvimento e validação](docs/DESENVOLVIMENTO.md)

## Dados e distribuição

Pareamento, recuperação, snapshots e cache ficam em `%LOCALAPPDATA%/LazyGaming`, fora do Git/WGT. Nunca exponha a API à Internet; ela usa HTTP/HMAC em LAN confiável. Encerrar pede confirmação; forçar exige confirmar possível perda de progresso. Telas/áudio são restaurados ao terminar a sessão.

O runtime Moonlight compilado é baixado separadamente da versão oficial 1.19.0 e conferido com SHA-256; não entra no Git. `Obter-Motor.py --wgt caminho/Moonlight.wgt` permite usar uma cópia oficial local. Código GPL-3.0; avisos e condições dos componentes em [THIRD_PARTY.md](THIRD_PARTY.md) e [LICENSE](LICENSE). O CI verifica build/testes e prepara o executável sem instalar ou publicar pacotes binários.
