"""Adapta apenas o WGT oficial fixado para uso local sem serviço de boot.
Não instala, não inicia apps e não acessa certificados/credenciais.
Uso futuro: python Preparar-Moonlight.py ORIGEM.wgt NOVA_PASTA
"""
import hashlib
from pathlib import Path
import sys
import zipfile
from lxml import etree

def adapt_javascript(text):
    guard = 'function handleDeepLink() {\n  if (!_isSmartHubSupported) {\n    return;\n  }'
    start = 'function probeSmartHubSupport() {'
    end = '\nfunction onWindowLoad() {'
    if text.count(guard) != 1 or text.count(start) != 1 or text.count(end) != 1:
        raise ValueError('Código de deep link/serviço divergente; revisar antes de adaptar.')
    text = text.replace(guard, 'function handleDeepLink() {\n  // LazyGaming: AppControl independente do serviço opcional de Smart Hub.')
    first, last = text.index(start), text.index(end)
    if first >= last:
        raise ValueError('Ordem inesperada das funções do Moonlight.')
    # Não basta remover o manifesto: o probe original lança o serviço ao abrir.
    return text[:first] + ('function probeSmartHubSupport() {\n'
        '  _isSmartHubSupported = false;\n'
        '  return Promise.resolve();\n}\n') + text[last:]


def main():
    expected = 'b5e64dbf0c79678400d82078d785a27073adf3cf08254843a97517312c0e184e'
    source, stage = Path(sys.argv[1]).resolve(), Path(sys.argv[2]).resolve()
    prepare(source, stage, expected)


def prepare(source, stage, expected):
    # Validar as adaptações antes de criar qualquer arquivo de destino.
    if hashlib.sha256(source.read_bytes()).hexdigest() != expected:
        raise SystemExit('Pacote diferente do Moonlight1.19.0 auditado.')
    with zipfile.ZipFile(source) as archive:
        adapted = adapt_javascript(archive.read('platform/index.js').decode('utf-8'))
    extract(source, stage, adapted)


def extract(source, stage, adapted):
    stage.mkdir(parents=True, exist_ok=False)
    with zipfile.ZipFile(source) as archive:
        for name in archive.namelist():
            if name.endswith('/') or name in {'author-signature.xml', 'signature1.xml'} or name.startswith('service/'):
                continue
            target = (stage / name).resolve()
            if not target.is_relative_to(stage):
                raise SystemExit('Caminho inválido no pacote.')
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(archive.read(name))
    config = stage / 'config.xml'
    tree = etree.parse(str(config))
    for node in tree.xpath('//t:service|//t:metadata[@key="http://samsung.com/tv/metadata/use.preview"]', namespaces={'t': 'http://tizen.org/ns/widgets'}):
        node.getparent().remove(node)
    tree.write(str(config), encoding='UTF-8', xml_declaration=True)
    (stage / 'platform/index.js').write_text(adapted, encoding='utf-8')
    print('Staging local preparado; assinatura, instalação e teste continuam separados.')


if __name__ == '__main__':
    main()