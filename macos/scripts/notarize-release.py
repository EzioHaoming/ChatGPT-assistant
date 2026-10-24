"""Developer ID release pipeline. Requires a Keychain identity and notary profile."""
import argparse,json,shutil,subprocess
from pathlib import Path

parser=argparse.ArgumentParser()
parser.add_argument('--identity',required=True,help='Developer ID Application certificate SHA-1')
parser.add_argument('--keychain-profile',required=True,help='Existing notarytool Keychain profile; never a password')
args=parser.parse_args()
root=Path(__file__).resolve().parents[1]
identities=subprocess.check_output(['security','find-identity','-v','-p','codesigning'],text=True)
if not any(args.identity in line and 'Developer ID Application:' in line for line in identities.splitlines()):
    raise SystemExit('Valid Developer ID Application identity required. Local certificate is not a public release identity.')
source=root/'dist/ChatGPT assistant.app'
subprocess.run(['codesign','--verify','--deep','--strict',str(source)],check=True)
stage=root/'build/public-release'
if stage.exists(): shutil.rmtree(stage)
stage.mkdir(parents=True)
app=stage/source.name
subprocess.run(['ditto','--norsrc',str(source),str(app)],check=True)
for p in app.rglob('*'):
    if p.name in {'Icon\r','avatar.png','avatar-undo.bin','settings.json','auth.json','identity.sha1','certificate.pem'}:
        raise SystemExit('Personal file detected: '+p.name)
# Sign nested Mach-O files first. Do not weaken hardened runtime protections.
magic={b'\xfe\xed\xfa\xce',b'\xce\xfa\xed\xfe',b'\xfe\xed\xfa\xcf',b'\xcf\xfa\xed\xfe',b'\xca\xfe\xba\xbe',b'\xbe\xba\xfe\xca',b'\xca\xfe\xba\xbf',b'\xbf\xba\xfe\xca'}
for p in sorted(app.rglob('*'),key=lambda x:len(x.parts),reverse=True):
    if p.is_symlink() or not p.is_file():continue
    with p.open('rb') as f:header=f.read(4)
    if header in magic:
        subprocess.run(['codesign','--force','--sign',args.identity,'--options','runtime','--timestamp',str(p)],check=True)
# Reset local certificate-pinned requirement; Developer ID supplies a stable team requirement.
subprocess.run(['codesign','--force','--sign',args.identity,'--options','runtime','--timestamp',str(app)],check=True)
subprocess.run(['codesign','--verify','--deep','--strict',str(app)],check=True)
archive=stage/'submission.zip'
subprocess.run(['ditto','-c','-k','--norsrc','--keepParent',str(app),str(archive)],check=True)
result=subprocess.run(['xcrun','notarytool','submit',str(archive),'--keychain-profile',args.keychain_profile,'--wait','--output-format','json'],capture_output=True,text=True)
(stage/'notary-result.json').write_text(result.stdout)
if result.returncode:raise SystemExit('Notary submission failed; check credentials and notary-result.json.')
record=json.loads(result.stdout)
if record.get('status')!='Accepted':
    subprocess.run(['xcrun','notarytool','log',record['id'],'--keychain-profile',args.keychain_profile,str(stage/'notary-log.json')],check=False)
    raise SystemExit('Notarization not accepted. No public release produced.')
subprocess.run(['xcrun','stapler','staple',str(app)],check=True)
subprocess.run(['xcrun','stapler','validate',str(app)],check=True)
subprocess.run(['spctl','--assess','--type','execute','--verbose=2',str(app)],check=True)
out=root/'dist-public';out.mkdir(exist_ok=True)
subprocess.run(['ditto','-c','-k','--norsrc','--keepParent',str(app),str(out/'ChatGPT-assistant-arm64.zip')],check=True)
print('Developer ID signed, notarized, stapled and Gatekeeper accepted release:',out)
