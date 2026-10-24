import io
import json
import unittest
from unittest.mock import patch
from test_model_settings import api, core

class APICatalog(unittest.TestCase):
    def fetch(self, config, data):
        with patch.object(api.urllib.request, 'build_opener') as opener:
            opener.return_value.open.return_value = io.BytesIO(json.dumps(data).encode())
            rows = api.models(config)
            request = opener.return_value.open.call_args.args[0]
            return rows, request

    def test_draft_credentials_and_ids_without_saving(self):
        with patch.object(core, 'credential_store') as store, patch.object(api, 'configure') as save:
            rows, request = self.fetch({'url':'https://example.com/v1/', 'key':'draft-only'},
                {'data':[{'id':'vendor/model-v1'}, {'id':'a'}, {'id':'a'}, {}, {'id':None}]})
            self.assertEqual([r['id'] for r in rows], ['a','vendor/model-v1'])
            self.assertEqual(request.full_url, 'https://example.com/v1/models')
            self.assertEqual(request.get_header('Authorization'), 'Bearer draft-only')
            store.assert_not_called(); save.assert_not_called()

    def test_blank_key_uses_only_matching_endpoint(self):
        with patch.object(core, 'credential_store') as store:
            store.return_value.get_password.return_value = 'saved-only'
            rows, request = self.fetch({'url':'https://other.example/v1'}, {'data':[{'id':'vision', 'input_modalities':['text','image']}]})
            store.return_value.get_password.assert_called_once_with('ChatGPT-assistant-custom-api', api.key_id('https://other.example/v1'))
            self.assertTrue(rows[0]['image'])
            self.assertEqual(request.get_header('Authorization'), 'Bearer saved-only')

    def test_missing_key_never_sends_request(self):
        with patch.object(core, 'credential_store') as store, patch.object(api.urllib.request, 'build_opener') as opener:
            store.return_value.get_password.return_value = None
            with self.assertRaises(ValueError): api.models({'url':'https://example.com/v1'})
            opener.assert_not_called()

    def test_empty_or_invalid_catalog_is_error(self):
        for data in ({'data':[]}, {'data':[{}, {'id':7}]}, {'error':'no models'}):
            with self.assertRaises(ValueError): self.fetch({'url':'https://example.com/v1','key':'test'},data)
