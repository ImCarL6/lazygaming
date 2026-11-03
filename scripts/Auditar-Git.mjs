import { execFileSync } from 'node:child_process';

// Inspect the actual Git index, not untracked private data or a different working copy.
const files = execFileSync('git', ['ls-files', '-z'], { encoding: 'utf8' }).split('\0').filter(Boolean);
if (!files.length) throw Error('Índice Git vazio; prepare os arquivos antes de auditar.');
const forbidden = /(?:^|\/)(?:outputs|dist|bin|obj|private|certificates|\.codex-remote-attachments)(?:\/|$)|(?:^|\/)(?:\.env(?:\..*)?|pairing\.json|tv-recovery\.json|session\.json|admin\.json|AGENTS\.md)$|\.(?:p12|pfx|pem|key|pwd|crt|csr|wgt|wasm|mem|dll|exe)$/i;
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
    'docs/PROMPT-HENRIQUE-0.15.md', 'docs/ESTADO-DERIVADO-MODELO.md', 'docs/VERSOES-DE-JOGO.md'
  ].includes(file);
  if (forbidden.test(file) || privateDoc) { console.error('Arquivo privado/produto de build no índice:', file); failures++; continue; }
  const bytes = execFileSync('git', ['show', `:${file}`], { maxBuffer: 16 * 1024 * 1024 });
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
