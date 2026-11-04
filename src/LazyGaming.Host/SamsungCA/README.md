# Certificados públicos da Samsung

Estas duas autoridades certificadoras da Samsung Certificate Extension compõem a cadeia dos certificados de TV criados localmente pelo usuário. São certificados X.509 públicos: não contêm chave privada, senha, conta ou identidade de PC/TV.

SHA-256 do certificado DER (independente das quebras de linha do PEM):

| Arquivo | SHA-256 |
| --- | --- |
| `vd_tizen_dev_author_ca.cer` | `5079196a1c32872a8bce74523fc12ad3557e2d87c56fafba7c6d09dced13b710` |
| `vd_tizen_dev_public2.crt` | `bf18859eb024cc1d63f008dcddd643077b0de53e958f35993ff0088d1a1d31ae` |

`scripts/Auditar-Git.mjs` aceita somente esses conteúdos nestes caminhos. Certificados pessoais, chaves e senhas continuam fora do Git e dos pacotes distribuídos.
