import { execFileSync } from 'node:child_process';
import { X509Certificate } from 'node:crypto';

// Somente estas duas CAs públicas, identificadas pelo certificado DER. Chaves e
// certificados pessoais continuam proibidos, inclusive dentro de SamsungCA.
const publicCa = new Map([
  ['src/LazyGaming.Host/SamsungCA/vd_tizen_dev_author_ca.cer','5079196a1c32872a8bce74523fc12ad3557e2d87c56fafba7c6d09dced13b710'],
  ['src/LazyGaming.Host/SamsungCA/vd_tizen_dev_public2.crt','bf18859eb024cc1d63f008dcddd643077b0de53e958f35993ff0088d1a1d31ae']
]);

// Inspect the actual Git index, not untracked private data or a different working copy.
const files = execFileSync('git', ['ls-files', '-z'], { encoding: 'utf8' }).split('\0').filter(Boolean);
if (!files.length) throw Error('Índice Git vazio; prepare os arquivos antes de auditar.');
const forbidden = /(?:^|\/)(?:outputs|dist|bin|obj|private|certificates|\.codex-remote-attachments)(?:\/|$)|(?:^|\/)(?:\.env(?:\..*)?|pairing\.json|tv-recovery\.json|session\.json|admin\.json|pc-command\.json|AGENTS\.md)$|\.(?:p12|pfx|pem|key|pwd|crt|csr|wgt|wasm|mem|dll|exe)$/i;
const secrets = [
  /-----BEGIN (?:RSA |EC |OPENSSH |ENCRYPTED )?PRIVATE KEY-----/,
  /gh[pousr]_[A-Za-z0-9]{30,}/,
  /github_pat_[A-Za-z0-9_]{40,}/,
  /AKIA[0-9A-Z]{16}/,
  /https:\/\/[^\s/@:]+:[^\s/@]+@github\.com/i
];
let failures = 0;
for (const file of files) {
  const privateDoc = file.startsWith('docs/') && ![
    'docs/ARQUITETURA.md', 'docs/CONTROLES.md', 'docs/DESENVOLVIMENTO.md',
    'docs/MIGRACAO-0.15.md', 'docs/INTEGRACAO-TV.md', 'docs/ATUALIZACOES-COMPARTILHADAS.md',
    'docs/PROMPT-HENRIQUE-0.15.md', 'docs/ESTADO-DERIVADO-MODELO.md', 'docs/VERSOES-DE-JOGO.md', 'docs/PC-PARA-AMIGOS.md'
  ].includes(file);
  if ((forbidden.test(file) && !publicCa.has(file)) || privateDoc) { console.error('Arquivo privado/produto de build no índice:', file); failures++; continue; }
  const bytes = execFileSync('git', ['show', `:${file}`], { maxBuffer: 16 * 1024 * 1024 });
  if (publicCa.has(file)) {
    try {
      if (!/^\s*-----BEGIN CERTIFICATE-----[A-Za-z0-9+/=\r\n]+-----END CERTIFICATE-----\s*$/.test(bytes.toString('utf8')) ||
          new X509Certificate(bytes).fingerprint256.replaceAll(':','').toLowerCase() !== publicCa.get(file)) throw Error();
    } catch { console.error('CA pública diferente da origem fixada:', file); failures++; }
    continue;
  }
  if (file.endsWith('.wav')) {
    if (!file.startsWith('tv/assets/audio/') || bytes.subarray(0, 4).toString() !== 'RIFF') { console.error('Áudio inesperado:', file); failures++; }
    continue;
  }
  if (bytes.includes(0) || secrets.some(pattern => pattern.test(bytes.toString('utf8')))) {
    console.error('Conteúdo binário/possível segredo; revisar sem imprimir conteúdo:', file); failures++;
  }
}
if (failures) process.exitCode = 1;
else console.log(`PASS: ${files.length} arquivos do índice; sem dados privados, binários de instalação ou padrões de segredo conhecidos.`);
