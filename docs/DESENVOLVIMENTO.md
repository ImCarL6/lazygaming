# Desenvolvimento e verificação

## Ambiente e comandos

Use Windows com SDK .NET 10, WebView2 Runtime, Node 22+, Python 3.12+ e PowerShell. `global.json` aceita a faixa de recursos mais recente do SDK 10.0. Dependências Python de desenvolvimento: `python -m pip install -r requirements-dev.txt`.

```powershell
dotnet restore LazyGaming.slnx --packages outputs/nuget
powershell -NoProfile -File scripts/Testar.ps1
python scripts/Obter-Motor.py
powershell -NoProfile -File scripts/Publicar-PC.ps1
powershell -NoProfile -File scripts/Empacotar-TV.ps1 -StageOnly
```

Os testes do núcleo são portáveis: `dotnet run --project tests/Core.Tests`. O Host e os testes completos exigem Windows. O script usa build serial/compilador local para evitar dependência de servidor de compilação persistente; não modifica configurações globais.

`Testar.ps1` compila a solução, roda Core.Tests/Host.Tests, todos os testes `.mjs` e a adaptação Moonlight Python. Integrações HTTP iniciam auxiliares `--test` em portas próprias, usam dados/segredos temporários e processos simulados, encerrando os próprios filhos. Nenhum teste deve parear um navegador com o auxiliar de produção.

Para prévia visual, `--preview-library` usa porta 47832 e dados isolados. Ela não inicia/encerra jogos reais nem muda telas/áudio. Não substitua build em uso; a publicação padrão em `outputs/pc-package` não toca `dist/LazyGaming`.

## Empacotamento

`Obter-Motor.py` baixa exclusivamente o Moonlight 1.19.0 oficial, confere SHA-256 fixado **antes** de extrair quatro arquivos de runtime e não executa código nem instala apps. Pode receber `--wgt` e `--destination`. Arquivos de runtime ficam ignorados no Git.

PC: `Publicar-PC.ps1` publica Release dependente dos runtimes Desktop/ASP.NET Core 10, copia a interface e licenças. Um pacote novo contém atalho local para `--open-launcher`; o auxiliar também pode ser iniciado com `--lan`. Não instala autostart. Verifique a pasta preparada antes de substituir uma versão em uso.

TV: `Empacotar-TV.ps1 -StageOnly` não exige CLI nem assinatura. Para gerar WGT, forneça `-TizenCli` e `-SigningProfile`, ou variáveis `LAZYGAMING_TIZEN_CLI`/`LAZYGAMING_TIZEN_PROFILE`. O perfil já deve existir localmente; senha/chaves não são argumentos do script. O script não instala o WGT na TV. `Verificar-Pacote.py` confere conteúdo/fontes, referências SHA-512, validade de certificados e assinaturas; isso não comprova instalação ou funcionamento físico.

## Git e CI

`node scripts/Auditar-Git.mjs` examina os bytes do **índice Git**: bloqueia caminhos privados/produtos de build e padrões conhecidos de chaves/tokens, sem imprimir conteúdo suspeito. É uma verificação complementar; revisar nomes e diff continua necessário.

README, arquitetura, controles e este guia são públicos. `AGENTS.md`, estado operacional, inventário da TV, diagnósticos pessoais, anexos, caches, backups, certificados e pareamentos permanecem locais/ignorados. Nunca usar `git add -f` nesses caminhos. Não colocar tokens em remotos ou argumentos.

CI Windows usa permissões somente de leitura: restaura dependências, audita o índice, obtém runtime com checksum, compila, executa testes isolados e verifica a publicação PC. Não instala, assina, distribui executáveis ou faz deploy. Uma execução verde remota é evidência distinta do teste local.

## Aceite antes de instalar esta reorganização

Conferir navegação PC/Samsung, microfone USB/BT completo, fallback Create+Options no BT básico, movimento e pressão de um/dois dedos, perda/retorno de foco, desconexão/reconexão, abertura/retorno e restauração de sessão Cabo/Stream. Jogos com entrada em background continuam sem isolamento universal. A execução física desta rodada é pendente até registro explícito no estado local.
