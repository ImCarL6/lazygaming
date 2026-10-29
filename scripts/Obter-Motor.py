"""Obtém só o runtime upstream auditado; não instala, assina ou executa nada."""
import argparse
import hashlib
from pathlib import Path
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parents[1]
URL = 'https://github.com/brightcraft/moonlight-tizen/releases/download/v1.19.0/Moonlight.wgt'
SHA256 = 'b5e64dbf0c79678400d82078d785a27073adf3cf08254843a97517312c0e184e'
FILES = ('moonlight-wasm.js', 'moonlight-wasm.js.mem', 'moonlight-wasm.wasm', 'moonlight-wasm.worker.js')


def prepare(source, destination):
    if hashlib.sha256(source.read_bytes()).hexdigest() != SHA256:
        raise ValueError('Pacote diferente do Moonlight 1.19.0 auditado; nenhum arquivo foi alterado.')
    with zipfile.ZipFile(source) as archive:
        payloads = {}
        for name in FILES:
            matches = [entry for entry in archive.namelist() if entry.rsplit('/', 1)[-1] == name]
            if len(matches) != 1:
                raise ValueError('Runtime ausente/ambíguo no pacote: ' + name)
            payloads[name] = archive.read(matches[0])
    destination.mkdir(parents=True, exist_ok=True)
    for name, payload in payloads.items():
        target = destination / name
        target.write_bytes(payload)
    print('PASS: runtime oficial 1.19.0 preparado com SHA-256 verificado; sem instalação.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--wgt', type=Path, help='Pacote oficial local; omita para baixar a versão fixada.')
    parser.add_argument('--destination', type=Path, default=ROOT / 'tv' / 'stream-engine')
    args = parser.parse_args()
    source = args.wgt
    if source is None:
        source = ROOT / 'outputs' / 'upstream' / 'Moonlight-1.19.0.wgt'
        source.parent.mkdir(parents=True, exist_ok=True)
        if not source.exists():
            request = urllib.request.Request(URL, headers={'User-Agent': 'LazyGaming-build'})
            with urllib.request.urlopen(request, timeout=120) as response:
                source.write_bytes(response.read())
    prepare(source, args.destination)
