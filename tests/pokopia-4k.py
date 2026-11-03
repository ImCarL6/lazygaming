"""Teste em memória: não altera configuração, saves ou executa emulador."""
import importlib.util
from pathlib import Path
import re

root = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('pokopia_config', root / 'scripts/Configurar-Pokopia.py')
config = importlib.util.module_from_spec(spec)
spec.loader.exec_module(config)
sample = ('[Controls]\nplayer_0_button_a="engine:sdl,button:0"\n'
          '[Renderer]\nresolution_setup\\use_global=true\nuse_vsync=2\n'
          '[System]\nlanguage_index=1\nuse_docked_mode\\use_global=true\n'
          '[Other]\nresolution_setup=3\nuse_docked_mode=0\n').encode()
allowed = {('Renderer', 'resolution_setup'), ('System', 'use_docked_mode')}
def protected(data):
    section = ''
    result = []
    for line in data.decode('utf-8-sig').splitlines(keepends=True):
        if line.startswith('['): section = line.strip()[1:-1]
        key = line.split('=', 1)[0].split('\\', 1)[0]
        if (section, key) not in allowed: result.append(line)
    return ''.join(result)
for data in [sample, sample.replace(b'\n', b'\r\n'), b'\xef\xbb\xbf' + sample]:
    after = config.fixed_4k_config(data)
    assert after == config.fixed_4k_config(after), 'Perfil deve ser idempotente.'
    assert protected(after) == protected(data), 'Controles/outras opções precisam ser preservados.'
    assert after.startswith(b'\xef\xbb\xbf') == data.startswith(b'\xef\xbb\xbf')
    assert b'resolution_setup=6' in after and b'use_docked_mode=1' in after
    assert b'resolution_setup\\use_global=false' in after and b'use_docked_mode\\use_global=false' in after
for data in [sample + b'[System]\n', sample.replace(b'[System]', b'[Renderer]'),
             sample.replace(b'use_docked_mode\\use_global=true', b'use_docked_mode=0\nuse_docked_mode=1')]:
    try: config.fixed_4k_config(data)
    except ValueError: pass
    else: raise AssertionError('Perfil ambíguo precisa ser recusado.')
print('PASS: 2x+docked por jogo; idempotência, BOM/CRLF, controles preservados e ambiguidades recusadas; nenhum arquivo do emulador escrito.')
