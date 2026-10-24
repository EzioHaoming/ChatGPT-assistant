import base64
import io
import json
from pathlib import Path
import sys
import unittest
from unittest.mock import patch
from PIL import Image
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'bridge'))
import api_backend as api
import desktop_bridge as bridge
import oauth_core as core

class Contracts(unittest.TestCase):
    def test_text_only(self):
        self.assertEqual(bridge.validate_request({'question':'hello'}), ('hello', None))
    def test_png(self):
        b=io.BytesIO(); Image.new('RGB',(8,8)).save(b,format='PNG')
        q,image=bridge.validate_request({'question':'translate','png':base64.b64encode(b.getvalue()).decode()})
        self.assertTrue(image.startswith('data:image/png;base64,'))
    def test_empty_question(self):
        with self.assertRaises(ValueError):bridge.validate_request({'question':''})
    def test_corrupt_png(self):
        with self.assertRaises(Exception):bridge.validate_request({'question':'hello','png':'not png'})
    def test_text_payload(self):
        self.assertEqual(len(api.payload('model','q',None)['messages'][0]['content']),1)
    def test_image_payload(self):
        self.assertEqual(api.payload('model','q','data:image/png;base64,AA==')['messages'][0]['content'][1]['type'],'image_url')
    def test_valid_endpoint(self):
        self.assertEqual(api.endpoint('https://example.com/v1/'),'https://example.com/v1/chat/completions')
    def test_bad_endpoint(self):
        for value in ['http://example.com','https://user:pw@example.com','https://example.com?k=x','https://{WorkspaceId}.example.com','https://example.com/chat/completions']:
            with self.subTest(value=value),self.assertRaises(ValueError):api.endpoint(value)
    def test_loopback(self):
        self.assertTrue(api.endpoint('http://127.0.0.1:1234/v1').startswith('http:'))
    def test_key_isolation(self):
        self.assertNotEqual(api.key_id('https://a.example/v1'),api.key_id('https://b.example/v1'))
    def test_oauth_state_validation(self):
        with self.assertRaises(ValueError):core.checked_callback({'state':['wrong']},'correct',None)
    def test_oauth_client_validation(self):
        with self.assertRaises(ValueError):core.checked_callback({'state':['s'],'client_id':['dynamic_agent_client'],'code':['c']},'s',None)
    def test_keychain_store_contract(self):
        class Memory:
            saved=None
            def set_password(self,s,a,v):self.saved=(s,a,v)
            def get_password(self,s,a):return self.saved[2]
        store=Memory()
        with patch.object(core,'credential_store',return_value=store):
            core.store_tokens({'access_token':'test-only-token'})
            self.assertEqual(core.load_tokens()['access_token'],'test-only-token')
        self.assertEqual(store.saved[0],'ChatGPT-assistant-independent-oauth')
    def test_no_redirect(self):
        self.assertIsNone(api.NoRedirect().redirect_request(None,None,None,None,None,None))
if __name__=='__main__':unittest.main()
