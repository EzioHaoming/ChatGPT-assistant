import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch
import sys
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'bridge'))
import api_backend as api
import oauth_core as core

class ModelSettings(unittest.TestCase):
    def test_channels_keep_separate_models(self):
        with tempfile.TemporaryDirectory() as folder, patch.object(api, 'FOLDER', Path(folder)), patch.object(core, 'credential_store') as keys:
            keys.return_value.get_password.return_value = 'test-only'
            api.configure({'api': False, 'model': 'chat-model'})
            api.configure({'api': True, 'model': 'api-model', 'url': 'https://example.com/v1', 'provider': 'custom'})
            self.assertEqual(api.settings()['chatgpt_model'], 'chat-model')
            api.configure({'api': True, 'model': 'api-model-2'})
            api.configure({'api': False, 'model': 'chat-model'})
            c = api.settings()
            self.assertEqual(c['api_model'], 'api-model-2')
            self.assertEqual(c['model'], 'chat-model')
            self.assertEqual(c['url'], 'https://example.com/v1')
    def test_legacy_chat_model_is_not_api_model(self):
        with tempfile.TemporaryDirectory() as folder, patch.object(api, 'FOLDER', Path(folder)):
            (Path(folder)/'settings.json').write_text(json.dumps({'api':False,'model':'old-chat'}))
            self.assertEqual(api.settings()['api_model'], '')
            self.assertEqual(api.settings()['chatgpt_model'], 'old-chat')
    def test_failed_api_save_keeps_previous_selection(self):
        with tempfile.TemporaryDirectory() as folder, patch.object(api, 'FOLDER', Path(folder)), patch.object(core,'credential_store') as keys:
            api.configure({'api':False,'model':'chat'})
            keys.return_value.get_password.return_value = None
            with self.assertRaises(ValueError):
                api.configure({'api':True,'model':'api','url':'https://example.com/v1'})
            self.assertEqual(api.settings()['model'],'chat')
