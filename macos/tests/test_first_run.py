import io,json,tempfile,unittest
from pathlib import Path
from unittest.mock import patch
from test_contracts import core,bridge,api

class FirstRun(unittest.TestCase):
    def test_new_install_no_settings_no_credentials(self):
        with tempfile.TemporaryDirectory() as tmp,patch.object(api,'FOLDER',Path(tmp)),patch.object(core,'credential_store') as store:
            store.return_value.get_password.return_value=None
            c=api.settings()
            self.assertFalse(c.get('api',False))
            self.assertEqual(c['api_model'],'')
            self.assertNotIn('codex',c)
            with self.assertRaisesRegex(RuntimeError,'OAuth'):core.access_token()
            self.assertFalse((Path(tmp)/'settings.json').exists())
    def test_login_selects_account_model_without_local_configuration(self):
        with tempfile.TemporaryDirectory() as tmp,patch.object(api,'FOLDER',Path(tmp)),patch.object(core,'login'),patch.object(core,'access_token',return_value='dummy'),patch.object(core,'http_json',return_value={'models':[{'slug':'available-model','visibility':'list','supported_in_api':True,'input_modalities':['text','image']}]}),patch.object(bridge.sys,'stdin',io.StringIO('{"action":"login"}')),patch.object(bridge,'emit') as emit:
            bridge.main()
            self.assertEqual(api.settings()['model'],'available-model')
            self.assertFalse(api.settings()['api'])
            self.assertNotIn('codex',api.settings())
            self.assertEqual(emit.call_args.kwargs['models'][0]['id'],'available-model')
