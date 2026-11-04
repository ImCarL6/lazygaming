import assert from 'node:assert/strict';
import {mkdtempSync,copyFileSync,mkdirSync,writeFileSync,rmSync} from 'node:fs';
import {execFileSync,spawnSync} from 'node:child_process';
import {tmpdir} from 'node:os';
import {join,resolve,sep,basename} from 'node:path';
const root=mkdtempSync(join(tmpdir(),'lg-public-ca-audit-')),audit=resolve('scripts/Auditar-Git.mjs');
try {
  execFileSync('git',['init','-q'],{cwd:root});
  const folder=join(root,'src/LazyGaming.Host/SamsungCA');mkdirSync(folder,{recursive:true});
  for(const name of ['vd_tizen_dev_author_ca.cer','vd_tizen_dev_public2.crt'])copyFileSync(resolve('src/LazyGaming.Host/SamsungCA',name),join(folder,name));
  const stage=()=>execFileSync('git',['add','-A'],{cwd:root});
  const run=()=>spawnSync(process.execPath,[audit],{cwd:root,encoding:'utf8'});
  stage();assert.equal(run().status,0,'somente as CAs públicas com fingerprint fixado são aceitas');
  const privateHeader='-----BEGIN '+'PRIVATE KEY-----';
  writeFileSync(join(folder,'vd_tizen_dev_public2.crt'),privateHeader+'\nnot-a-real-key\n-----END PRIVATE KEY-----');stage();assert.equal(run().status,1,'mesmo nome não permite substituir CA pública por chave privada');
  copyFileSync(resolve('src/LazyGaming.Host/SamsungCA/vd_tizen_dev_public2.crt'),join(folder,'vd_tizen_dev_public2.crt'));
  copyFileSync(resolve('src/LazyGaming.Host/SamsungCA/vd_tizen_dev_public2.crt'),join(folder,'personal.crt'));stage();assert.equal(run().status,1,'outros certificados continuam proibidos');
  rmSync(join(folder,'personal.crt'));writeFileSync(join(root,'secret.txt'),privateHeader+'\nnot-a-real-key');stage();assert.equal(run().status,1,'segredos fora da pasta também são recusados');
} finally {assert.ok(resolve(root).startsWith(resolve(tmpdir())+sep)&&basename(root).startsWith('lg-public-ca-audit-'));rmSync(root,{recursive:true,force:true});}
console.log('PASS: auditoria aceita apenas CAs públicas fixadas; mantém bloqueio de chaves/credenciais/certificados pessoais.');
