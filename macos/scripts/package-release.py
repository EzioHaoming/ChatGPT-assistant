"""Create clean app/source archives and verify their extracted contents."""
import hashlib, importlib.util, os, shutil, subprocess, tempfile, zipfile
from pathlib import Path
root=Path(__file__).resolve().parents[1]
spec=importlib.util.spec_from_file_location('hygiene',root/'scripts/release-hygiene.py')
hygiene=importlib.util.module_from_spec(spec);spec.loader.exec_module(hygiene)
app=root/'dist/ChatGPT assistant.app'
out=root/'artifacts';out.mkdir(exist_ok=True)
def verify(p):
    subprocess.run(['codesign','--verify','--deep','--strict',str(p)],check=True)
    subprocess.run([str(root/'runtime/bin/python3'),'-B',str(root/'scripts/sign-nested.py'),'--verify',str(p)],check=True)
def zipdir(src,dst):
    subprocess.run(['ditto','-c','-k','--norsrc','--keepParent',str(src),str(dst)],check=True)
hygiene.audit(app);verify(app)
appzip=out/'ChatGPT-assistant-arm64.zip';zipdir(app,appzip)
with zipfile.ZipFile(appzip,'a',compression=zipfile.ZIP_DEFLATED) as z:
    for p in [root/'LICENSE',root/'README.md',*sorted((root/'docs').rglob('*'))]:
        if p.is_file():z.write(p,str(p.relative_to(root)))
sourcezip=out/'ChatGPT-assistant-macOS-v0.1.2-source.zip'
with tempfile.TemporaryDirectory() as tmp:
    stage=Path(tmp)/'ChatGPT-assistant-macOS-v0.1.2-source';stage.mkdir()
    for name in ['Sources','Resources','bridge','runtime','scripts','tests','docs','LICENSE','README.md','SOURCE-BUILD.md','Info.plist','app.png','build.command','setup.command','.gitignore']:
        p=root/name;q=stage/name
        if p.is_dir():shutil.copytree(p,q,symlinks=True,ignore=shutil.ignore_patterns('__pycache__','*.pyc','*.pyo','.DS_Store'))
        else:shutil.copy2(p,q)
    hygiene.audit(stage)
    entries=[]
    for p in sorted(stage.rglob('*')):
        if p.is_symlink():b=os.readlink(p).encode()
        elif p.is_file():b=p.read_bytes()
        else:continue
        entries.append(hashlib.sha256(b).hexdigest()+'  '+str(p.relative_to(stage)))
    (stage/'SOURCES.sha256').write_text('\n'.join(entries)+'\n')
    zipdir(stage,sourcezip)
with tempfile.TemporaryDirectory() as tmp:
    for archive in [appzip,sourcezip]:
        dest=Path(tmp)/archive.stem;dest.mkdir()
        subprocess.run(['ditto','-x','-k',str(archive),str(dest)],check=True)
        hygiene.audit(dest)
        if archive==appzip:verify(dest/app.name)
(out/'SHA256.txt').write_text(''.join(hashlib.sha256(p.read_bytes()).hexdigest()+'  '+p.name+'\n' for p in [appzip,sourcezip]))
print('Verified app and source archives in artifacts/')
