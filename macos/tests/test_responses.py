import io,json,unittest
from unittest.mock import patch
from test_contracts import core,bridge
import responses_backend as r

def stream(*events):
    return io.BytesIO(b''.join(('data: '+json.dumps(e)+'\n\n').encode() for e in events))

def completed(text='success'):
    return {'type':'response.completed','response':{'status':'completed','output':[{'type':'message','content':[{'type':'output_text','text':text}]}]}}

class DirectResponses(unittest.TestCase):
    def test_text_image_and_no_storage(self):
        p=r.payload('model','hello','data:image/png;base64,AA==')
        self.assertEqual(p['input'][0]['content'][1]['type'],'input_image')
        self.assertFalse(p['store']);self.assertTrue(p['stream'])
        self.assertNotIn('tools',p)
    def test_completed_not_delta_is_success(self):
        with patch.object(r.urllib.request,'build_opener') as opener:
            opener.return_value.open.return_value=stream({'type':'response.output_text.delta','delta':'partial'},completed())
            deltas=[]
            self.assertEqual(r.ask('dummy','m','q',on_delta=deltas.append)['answer'],'success')
            self.assertEqual(deltas,['partial'])
            self.assertEqual(opener.return_value.open.call_args.args[0].full_url,'https://api.openai.com/v1/responses')
    def test_truncated_stream_fails(self):
        with patch.object(r.urllib.request,'build_opener') as opener:
            opener.return_value.open.return_value=stream({'type':'response.output_text.delta','delta':'partial'})
            with self.assertRaises(RuntimeError):r.ask('dummy','m','q')
    def test_late_failure_not_success(self):
        with patch.object(r.urllib.request,'build_opener') as opener:
            opener.return_value.open.return_value=stream({'type':'response.output_text.delta','delta':'partial'}, {'type':'response.failed','response':{'error':{'code':'subscription_sharing_usage_limit_exceeded'}}})
            with self.assertRaisesRegex(RuntimeError,'usage_limit'):r.ask('dummy','m','q')
    def test_default_browser_no_chrome_requirement(self):
        with patch.object(core.subprocess,'run') as run:
            core.open_browser('https://auth.openai.com/test')
            run.assert_called_once_with(['/usr/bin/open','https://auth.openai.com/test'],check=True)
    def test_clean_settings_no_codex_required(self):
        with patch.object(bridge.api_backend,'settings',return_value={}),patch.object(core,'access_token',return_value='dummy'),patch.object(r,'ask',return_value={'answer':'ok','usage':None}) as ask,patch.object(bridge.sys,'stdin',io.StringIO(json.dumps({'action':'ask','question':'q'}))),patch.object(bridge,'emit') as emit:
            bridge.main()
            ask.assert_called_once()
            emit.assert_called_with('done',answer='ok',usage=None)

    def test_completed_can_omit_previously_streamed_output(self):
        with patch.object(r.urllib.request,'build_opener') as opener:
            opener.return_value.open.return_value=stream({'type':'response.output_text.delta','delta':'finished'}, {'type':'response.completed','response':{'status':'completed','output':[]}})
            self.assertEqual(r.ask('dummy','m','q')['answer'],'finished')
