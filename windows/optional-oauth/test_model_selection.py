import base64
import io
import json
import unittest
from unittest.mock import patch, MagicMock
from PIL import Image
import desktop_bridge as bridge

class ModelSelectionTests(unittest.TestCase):
    def test_catalog_filters_hidden_and_non_image(self):
        entries=[dict(slug='vision', visibility='list', supported_in_api=True, input_modalities=['text','image']),
                 dict(slug='hidden', visibility='hide', supported_in_api=True, input_modalities=['image']),
                 dict(slug='text', visibility='list', supported_in_api=True, input_modalities=['text'])]
        self.assertEqual(bridge.image_models({'models':entries}), ['vision'])

    def test_default_and_invalid_model(self):
        self.assertEqual(bridge.selected_model({}), 'gpt-6-luna')
        with self.assertRaises(ValueError): bridge.selected_model({'model':'bad\nmodel'})

    def test_selected_model_reaches_server_with_png(self):
        png=io.BytesIO();Image.new('RGB',(4,4),'white').save(png,format='PNG')
        for model in ['gpt-6-luna','gpt-6-sol']:
            with self.subTest(model=model):
                request=dict(action='ask',model=model,question='Read this image',png=base64.b64encode(png.getvalue()).decode(),codex='test')
                server=MagicMock();server.ask.return_value=dict(status='completed',answers=['ok'],usage=None)
                with patch.object(bridge.sys,'stdin',io.StringIO(json.dumps(request))), patch.object(bridge.core,'access_token',return_value='test-only'), patch.object(bridge.core,'Server',return_value=server), patch.object(bridge,'emit'):
                    bridge.main()
                self.assertEqual(server.ask.call_args.args[0], model)
                self.assertTrue(server.ask.call_args.kwargs['image_data'].startswith('data:image/png;base64,'))
                server.close.assert_called_once()

    def test_no_fallback_when_selected_model_fails(self):
        server=MagicMock();server.ask.side_effect=RuntimeError('model unsupported')
        with patch.object(bridge.sys,'stdin',io.StringIO(json.dumps(dict(model='gpt-6-sol',codex='test')))), patch.object(bridge,'validate_request',return_value=('test','data:image/png;base64,test')), patch.object(bridge.core,'access_token',return_value='test-only'), patch.object(bridge.core,'Server',return_value=server):
            with self.assertRaises(RuntimeError):bridge.main()
        server.ask.assert_called_once();server.close.assert_called_once()

if __name__=='__main__':unittest.main()
