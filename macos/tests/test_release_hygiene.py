import importlib.util, tempfile, unittest, py_compile
from pathlib import Path
spec=importlib.util.spec_from_file_location('release_hygiene',Path(__file__).resolve().parents[1]/'scripts/release-hygiene.py')
h=importlib.util.module_from_spec(spec);spec.loader.exec_module(h)
class ReleaseHygiene(unittest.TestCase):
 def test_cache_is_removed_but_source_preserved(self):
  with tempfile.TemporaryDirectory() as d:
   p=Path(d)/'module.py';p.write_text('value=42\n');py_compile.compile(str(p),dfile=str(Path.home()/'private-build/module.py'))
   with self.assertRaises(RuntimeError):h.audit(d)
   h.audit(d,clean=True);self.assertTrue(p.exists());self.assertFalse(list(Path(d).rglob('*.pyc')))
 def test_sourceless_cache_is_not_silently_deleted(self):
  with tempfile.TemporaryDirectory() as d:
   p=Path(d)/'module.pyc';p.write_bytes(b'bytecode')
   with self.assertRaises(RuntimeError):h.audit(d,clean=True)
   self.assertTrue(p.exists())
 def test_personal_path_rejected(self):
  with tempfile.TemporaryDirectory() as d:
   (Path(d)/'data').write_text(str(Path.home()/'private-build'))
   with self.assertRaises(RuntimeError):h.audit(d)
 def test_credentials_and_user_files_rejected(self):
  with tempfile.TemporaryDirectory() as d:
   (Path(d)/'auth.json').write_text('{}')
   with self.assertRaises(RuntimeError):h.audit(d)
