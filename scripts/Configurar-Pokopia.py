"""Configuração explícita do perfil Pokopia/Eglantine; sem caminhos pessoais ou instalação implícita."""
from pathlib import Path
import json, shutil, re, hashlib, os

def resolution_config(data):
    """Altera apenas Renderer/resolution_setup, preservando bytes das demais seções."""
    text = data.decode('utf-8')
    sections = list(re.finditer(r'(?m)^\[([^\]\r\n]+)\]\r?$', text))
    renderers = [i for i, section in enumerate(sections) if section[1] == 'Renderer']
    if len(renderers) != 1:
        raise ValueError('Esperada exatamente uma seção Renderer.')
    index = renderers[0]
    begin = sections[index].end()
    end = sections[index + 1].start() if index + 1 < len(sections) else len(text)
    block = text[begin:end]
    newline = '\r\n' if '\r\n' in block else '\n'
    # Enum 6=Res2X; não é prova da resolução interna do jogo.
    for key, value in [('resolution_setup\\use_global', 'false'),
                       ('resolution_setup\\default', 'false'), ('resolution_setup', '6')]:
        pattern = r'(?m)^' + re.escape(key) + r'=[^\r\n]*'
        matches = list(re.finditer(pattern, block))
        if len(matches) > 1:
            raise ValueError('Chave duplicada: ' + key)
        if matches:
            block = re.sub(pattern, lambda _: key + '=' + value, block)
        else:
            if not block.endswith('\n'):
                block += newline
            block += key + '=' + value + newline
    return (text[:begin] + block + text[end:]).encode('utf-8')

def fixed_4k_config(data):
    """Perfil independente: 2x e docked, sem alterar controles ou opções globais."""
    text = resolution_config(data).decode('utf-8')
    sections = list(re.finditer(r'(?m)^\[([^\]\r\n]+)\]\r?$', text))
    system = [i for i, section in enumerate(sections) if section[1] == 'System']
    if len(system) != 1:
        raise ValueError('Esperada exatamente uma seção System.')
    index = system[0]
    begin = sections[index].end()
    end = sections[index + 1].start() if index + 1 < len(sections) else len(text)
    block = text[begin:end]
    newline = '\r\n' if '\r\n' in block else '\n'
    for key, value in [('use_docked_mode\\use_global', 'false'),
                       ('use_docked_mode\\default', 'false'), ('use_docked_mode', '1')]:
        pattern = r'(?m)^' + re.escape(key) + r'=[^\r\n]*'
        matches = list(re.finditer(pattern, block))
        if len(matches) > 1:
            raise ValueError('Chave duplicada: ' + key)
        if matches:
            block = re.sub(pattern, lambda _: key + '=' + value, block)
        else:
            if not block.endswith('\n'):
                block += newline
            block += key + '=' + value + newline
    return (text[:begin] + block + text[end:]).encode('utf-8')


def configure(emulator_directory, content_path, apply=False, backup=None, expected=None, fixed_4k=False):
    installation = Path(emulator_directory)
    game_content = Path(content_path)
    valid_content = game_content.is_file() and Path(str(game_content) + '.npdm').is_file()
    if game_content.is_file() and game_content.suffix.lower() == '.nup':
        with game_content.open('rb') as stream:
            valid_content = stream.read(4) == b'NUP\x01' and game_content.stat().st_size >= 128
    if not valid_content or not (installation / 'eglantine.exe').is_file():
        raise ValueError('Conteúdo existente não encontrado.')
    p = installation / 'user/config/custom/0400A3A01EE4C000.ini'
    before = p.read_bytes()
    after = fixed_4k_config(before) if fixed_4k else resolution_config(before)
    digest = hashlib.sha256(before).hexdigest()
    plan = {'file': str(p), 'beforeSha256': digest,
            'afterSha256': hashlib.sha256(after).hexdigest(), 'changed': before != after,
            'scope': 'Perfil Pokopia: 2x+docked, independente do global.' if fixed_4k else 'Somente escala por jogo; -f já solicita fullscreen no lançamento.',
            'fixed4kProfile': fixed_4k,
            'internal4kValidated': False, 'applied': False}
    if apply:
        # Verificar o processo real no mesmo contexto que fará a alteração.
        import subprocess
        probe = subprocess.run(['tasklist.exe', '/FI', 'IMAGENAME eq eglantine.exe', '/FO', 'CSV', '/NH'],
                               capture_output=True, check=True)
        if b'eglantine.exe' in probe.stdout.lower():
            raise ValueError('Feche o Eglantine antes de alterar o perfil; o emulador aberto pode regravar o INI.')
        if not backup or expected != digest:
            raise ValueError('Backup e hash da configuração revisada são obrigatórios.')
        dest = Path(backup) / '0400A3A01EE4C000.ini'
        dest.parent.mkdir(parents=True, exist_ok=True)
        if dest.exists():
            raise FileExistsError('Backup já existe; não sobrescrever.')
        shutil.copy2(p, dest)
        if dest.read_bytes() != before or p.read_bytes() != before:
            raise ValueError('Configuração mudou durante o backup.')
        if before != after:
            temporary = p.with_name(p.name + '.lazygaming.tmp')
            created = False
            try:
                with temporary.open('xb') as stream:
                    created = True
                    stream.write(after)
                os.replace(temporary, p)
            finally:
                if created:
                    temporary.unlink(missing_ok=True)
        plan['applied'] = True
    print(json.dumps(plan, ensure_ascii=False))

if __name__ == '__main__':
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument('--emulator-directory', required=True)
    parser.add_argument('--content-path', required=True)
    parser.add_argument('--apply', action='store_true')
    parser.add_argument('--backup-directory')
    parser.add_argument('--expected-sha256')
    parser.add_argument('--fixed-4k', action='store_true')
    args = parser.parse_args()
    configure(args.emulator_directory, args.content_path, args.apply, args.backup_directory,
              args.expected_sha256, args.fixed_4k)
