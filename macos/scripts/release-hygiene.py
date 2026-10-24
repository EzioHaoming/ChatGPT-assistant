"""Reject personal data and build caches before signing and packaging."""
import argparse, hashlib, re, shutil
from pathlib import Path

def audit(root, clean=False):
    root = Path(root).resolve()
    if clean:
        for p in sorted(root.rglob('__pycache__'), reverse=True):
            if p.is_dir() and not p.is_symlink():
                for cache in p.glob('*.pyc'):
                    source = p.parent/(cache.name.split('.')[0]+'.py')
                    if not source.exists(): raise RuntimeError('Cache without source: ' + str(cache.relative_to(root)))
                shutil.rmtree(p)
        for p in root.rglob('*'):
            if p.is_file() and (p.suffix in {'.pyc', '.pyo'} or p.name == '.DS_Store'):
                # Do not discard a sourceless module: that would break imports.
                if p.suffix in {'.pyc', '.pyo'} and not p.with_suffix('.py').exists():
                    raise RuntimeError('Sourceless bytecode requires explicit handling: ' + str(p.relative_to(root)))
                p.unlink()
    forbidden = {'avatar.png','avatar-undo.bin','settings.json','auth.json','identity.sha1','certificate.pem','Icon\r','.DS_Store'}
    private_dir = Path.home()/'Library/Application Support/ChatGPT assistant'
    avatar_hashes = {hashlib.sha256(p.read_bytes()).digest() for p in (private_dir/'avatar.png', private_dir/'avatar-undo.bin') if p.is_file() and p.stat().st_size}
    user = Path.home().name.encode()
    count = 0
    for p in root.rglob('*'):
        if p.is_symlink():
            if p.readlink().is_absolute(): raise RuntimeError('Absolute symlink: ' + str(p.relative_to(root)))
            continue
        if not p.is_file(): continue
        name = str(p.relative_to(root)); data = p.read_bytes(); count += 1
        if p.name in forbidden or p.suffix.lower() in {'.pyc','.pyo','.p12','.pfx','.key','.keychain','.keychain-db'}:
            raise RuntimeError('Forbidden release file: ' + name)
        if re.search(rb'/Users/(?!runner/)[A-Za-z0-9_.-]+/',data) or (len(user)>3 and user in data):
            raise RuntimeError('Personal build path/name in: ' + name)
        if re.search(rb'-----BEGIN (?:[A-Z ]+ )?PRIVATE KEY-----[\r\n]+[A-Za-z0-9+/=\r\n]{48,}|(?<![A-Za-z0-9-])sk-(?:proj-)?[A-Za-z0-9_-]{32,}|eyJ[A-Za-z0-9_-]{16,}\.[A-Za-z0-9_-]{16,}\.[A-Za-z0-9_-]{16,}', data):
            # Upstream PyJWT README has a public HS256 example ({some: payload}, secret).
            if hashlib.sha256(data).hexdigest() != '1ea0c9f139b8c928276b3628af165955f6f764491448a6ad8f103cdcbb382da3':
                raise RuntimeError('Possible credential/private key in: ' + name)
        if hashlib.sha256(data).digest() in avatar_hashes: raise RuntimeError('Personal avatar content in: ' + name)
    print('Release hygiene passed: ' + str(count) + ' files; no bytecode, personal paths or detected credentials')

if __name__ == '__main__':
    ap=argparse.ArgumentParser(); ap.add_argument('--clean',action='store_true'); ap.add_argument('root'); a=ap.parse_args(); audit(a.root,a.clean)
