# Componentes de terceiros

O código do LazyGaming é distribuído sob GPL-3.0; componentes de terceiros preservam suas licenças.

| Componente | Origem | Licença / distribuição |
| --- | --- | --- |
| Moonlight Tizen 1.19.0 | https://github.com/brightcraft/moonlight-tizen/tree/v1.19.0 | GPL-3.0, texto em tv/stream-engine/LICENSE-Moonlight. Runtime compilado obtido separadamente pelo script com SHA-256 fixado. |
| jQuery | Cabeçalho de tv/stream-engine/jquery.js | MIT, texto em tv/stream-engine/LICENSE-jQuery. |
| Microsoft WebView2 SDK 1.0.3856.49 | https://www.nuget.org/packages/Microsoft.Web.WebView2/1.0.3856.49 | Licença e avisos em src/LazyGaming.Host/ThirdParty; NuGet restaura os binários. Runtime Windows separado. |
| CAs públicas Samsung para assinatura de TV | Samsung Certificate Extension do Tizen Studio | Dois certificados X.509 públicos, sem chaves privadas; origem e fingerprints em src/LazyGaming.Host/SamsungCA/README.md. SDK e certificados pessoais não são redistribuídos. |

`engine.js`, `engine.html` e a interface pertencem à integração LazyGaming. `messages.js` retém o transporte RPC upstream; áudio/protocolo/WASM conservam origem e atribuição. Não há serviço de boot no pacote integrado.

Este repositório não distribui o WASM compilado. Quem redistribuir executáveis/WGT contendo componentes GPL deve fornecer o código-fonte correspondente completo e os meios de compilação exigidos pela licença. O link upstream identifica a origem; não substitui essa obrigação do distribuidor. O CI verifica build/testes sem distribuir runtime compilado, assinatura ou identidade privada. Artes do catálogo/jogos, SDK Tizen, certificados pessoais e dados de pareamento não fazem parte do repositório. A exceção são as duas CAs públicas identificadas acima.
