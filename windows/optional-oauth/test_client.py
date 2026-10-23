import os
from pathlib import Path
import tempfile
import queue
import unittest
from unittest.mock import patch
import vision_test as app


class Tests(unittest.TestCase):
    def test_dpapi_roundtrip(self):
        secret = b'private test credential'
        encrypted = app.protect(secret)
        self.assertNotIn(secret, encrypted)
        self.assertEqual(app.protect(encrypted, decrypt=True), secret)

    def test_callback(self):
        query = {'state': ['valid'], 'code': ['one-time'], 'client_id': ['issued']}
        self.assertEqual(app.checked_callback(query, 'valid'), ('issued', 'one-time'))
        for state, client in [('wrong', None), ('valid', 'different')]:
            with self.assertRaises(ValueError):
                app.checked_callback(query, state, client)
        with self.assertRaises(ValueError):
            app.checked_callback({'state': ['valid'], 'code': ['a', 'b']}, 'valid')
        with self.assertRaises(RuntimeError):
            app.checked_callback({'state': ['valid'], 'error': ['access_denied']}, 'valid')

    def test_scope_gate(self):
        with self.assertRaises(RuntimeError):
            app.require_scope({'scope': 'openid profile email'})
        app.require_scope({'scope': app.SCOPE})

    def test_environment_isolation(self):
        with patch.dict(os.environ, {'OPENAI_API_KEY': 'must-not-leak', 'CODEX_HOME': 'internal', 'CODEX_ACCESS_TOKEN': 'internal-token'}):
            env = app.child_environment('independent')
        self.assertNotIn('OPENAI_API_KEY', env)
        self.assertNotIn('CODEX_ACCESS_TOKEN', env)
        self.assertEqual(env['ACCESS_TOKEN'], 'independent')
        self.assertNotEqual(env['CODEX_HOME'], 'internal')

    def test_png_validation(self):
        from PIL import Image
        with tempfile.TemporaryDirectory() as folder:
            p = Path(folder) / 'sample.png'
            Image.new('RGB', (8, 8), 'red').save(p)
            self.assertEqual(app.validate_png(p), p.resolve())
            p.write_bytes(b'not a PNG')
            with self.assertRaises(Exception):
                app.validate_png(p)
            with self.assertRaises(FileNotFoundError):
                app.validate_png(Path(folder) / 'missing.png')

    def fake_server(self, status):
        server = app.Server.__new__(app.Server)
        server.rpc = lambda method, params: {'thread': {'id': 't'}} if method == 'thread/start' else {'turn': {'id': 'r'}}
        server.pending = [
            {'method': 'item/completed', 'params': {'threadId': 't', 'item': {'type': 'agentMessage', 'text': 'TEXT_OK'}}},
            {'method': 'thread/tokenUsage/updated', 'params': {'threadId': 't', 'tokenUsage': {'last': {'totalTokens': 12}}}},
            {'method': 'turn/completed', 'params': {'threadId': 't', 'turn': {'id': 'r', 'status': status, 'error': {'message': 'denied'} if status == 'failed' else None}}}
        ]
        return server

    def test_stream_completion_and_usage(self):
        result = self.fake_server('completed').ask('model', 'question')
        self.assertEqual(result['answers'], ['TEXT_OK'])
        self.assertEqual(result['usage']['last']['totalTokens'], 12)
        self.assertEqual(result['status'], 'completed')

    def test_partial_answer_is_not_success(self):
        result = self.fake_server('failed').ask('model', 'question')
        self.assertEqual(result['status'], 'failed')
        self.assertEqual(result['error']['message'], 'denied')

    def test_rpc_timeout(self):
        server = app.Server.__new__(app.Server)
        server.events = queue.Queue()
        with self.assertRaises(TimeoutError):
            server.receive(0.01)


if __name__ == '__main__':
    unittest.main()
