"""Sign or verify every nested Mach-O without exporting signing material."""
import subprocess, sys
from pathlib import Path
MAGIC = {b"\xfe\xed\xfa\xce", b"\xce\xfa\xed\xfe", b"\xfe\xed\xfa\xcf", b"\xcf\xfa\xed\xfe", b"\xca\xfe\xba\xbe", b"\xbe\xba\xfe\xca"}
verify = sys.argv[1] == '--verify'
app = Path(sys.argv[2] if verify else sys.argv[1])
count = 0
for p in sorted(app.rglob('*')):
    if p.is_symlink() or not p.is_file(): continue
    with p.open('rb') as f: magic = f.read(4)
    if magic not in MAGIC: continue
    cmd = ['codesign', '--verify', '--strict'] if verify else ['codesign', '--force', '--sign', sys.argv[2], '--timestamp=none']
    subprocess.run(cmd + [str(p)], check=True, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE)
    count += 1
print(('Verified' if verify else 'Signed') + ' nested Mach-O files: ' + str(count))
