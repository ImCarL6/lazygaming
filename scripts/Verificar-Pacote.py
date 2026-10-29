"""Confere conteúdo, hashes XMLDSig e assinaturas RSA do WGT sem abrir chaves privadas."""
import base64
import argparse
import hashlib
from pathlib import Path
import zipfile
from datetime import datetime, timezone
from urllib.parse import unquote
from lxml import etree
from cryptography import x509
from cryptography.hazmat.primitives import hashes
from cryptography.hazmat.primitives.asymmetric import padding

root = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--package', type=Path, default=root / 'outputs' / 'LazyGaming.wgt')
package = parser.parse_args().package
namespace = {'ds': 'http://www.w3.org/2000/09/xmldsig#'}
expected = {'app.js', 'platform.js', 'presentation.js', 'config.xml', 'crypto.js', 'index.html', 'style.css', 'stream.js', 'stream-policy.js', 'author-signature.xml', 'signature1.xml', 'LICENSE', 'THIRD_PARTY.md'}
expected.update(p.relative_to(root / 'tv').as_posix() for p in (root / 'tv' / 'assets').rglob('*') if p.is_file())
expected.update(p.relative_to(root / 'tv').as_posix() for p in (root / 'tv' / 'stream-engine').rglob('*') if p.is_file())
with zipfile.ZipFile(package) as z:
    assert z.testzip() is None
    assert {name for name in z.namelist() if not name.endswith('/')} == expected
    for name in expected - {'author-signature.xml', 'signature1.xml'}:
        assert z.read(name) == (root / name if name in {'LICENSE', 'THIRD_PARTY.md'} else root / 'tv' / name).read_bytes(), name
    for name in ['author-signature.xml', 'signature1.xml']:
        tree = etree.fromstring(z.read(name))
        signed = tree.find('ds:SignedInfo', namespace)
        assert signed is not None
        for ref in signed.findall('ds:Reference', namespace):
            uri = unquote(ref.get('URI'))
            if uri.startswith('#'):
                target, = tree.xpath('//*[@Id=$id]', id=uri[1:])
                transforms = ref.findall('ds:Transforms/ds:Transform', namespace)
                exclusive = any('xml-exc-c14n' in t.get('Algorithm', '') for t in transforms)
                target = etree.fromstring(etree.tostring(target))
                data = etree.tostring(target, method='c14n', exclusive=exclusive)
            else:
                assert uri in expected
                data = z.read(uri)
            algorithm = ref.find('ds:DigestMethod', namespace).get('Algorithm')
            assert algorithm.endswith('sha512')
            assert hashlib.sha512(data).digest() == base64.b64decode(ref.findtext('ds:DigestValue', namespaces=namespace)), (name, uri)
        cert = x509.load_der_x509_certificate(base64.b64decode(tree.findtext('.//ds:X509Certificate', namespaces=namespace)))
        assert cert.not_valid_before_utc <= datetime.now(timezone.utc) <= cert.not_valid_after_utc
        canonical = etree.tostring(signed, method='c14n', exclusive=True)
        cert.public_key().verify(base64.b64decode(tree.findtext('ds:SignatureValue', namespaces=namespace)), canonical, padding.PKCS1v15(), hashes.SHA512())
print('PASS: ZIP, fontes correspondentes, referências SHA-512, certificados no prazo e duas assinaturas RSA válidas.')
print('SHA256:', hashlib.sha256(package.read_bytes()).hexdigest())
print('A aceitação pelo dispositivo e aprovação funcional ainda dependem da TV.')
