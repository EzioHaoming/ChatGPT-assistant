import base64,io,unittest
from unittest.mock import patch
from PIL import Image
import desktop_bridge as bridge
import test_client
class BridgeTests(unittest.TestCase):
 def png(self):
  b=io.BytesIO();Image.new('RGB',(4,4),'red').save(b,format='PNG');return base64.b64encode(b.getvalue()).decode()
 def test_text_only(self):
  self.assertEqual(bridge.validate_request({'question':'Explain'}),('Explain',None))
 def test_memory_png(self):
  q,data=bridge.validate_request({'question':'Explain','png':self.png()});self.assertTrue(data.startswith('data:image/png;base64,'))
 def test_invalid_inputs(self):
  for r in ({'question':'','png':self.png()},{'question':'test','png':'invalid'},{'question':'x'*12001,'png':self.png()}):
   with self.assertRaises(ValueError):bridge.validate_request(r)
 def test_stream_association(self):
  s=test_client.Tests().fake_server('completed'); calls=[];deltas=[]
  def rpc(method,params):
   calls.append((method,params));return {'thread':{'id':'t'}} if method=='thread/start' else {'turn':{'id':'r'}}
  s.rpc=rpc;s.pending[0:0]=[{'method':'item/agentMessage/delta','params':{'threadId':'t','turnId':'wrong','delta':'bad'}},{'method':'item/agentMessage/delta','params':{'threadId':'t','turnId':'r','delta':'good'}}]
  result=s.ask('model','test',image_data='data:image/png;base64,abc',on_delta=deltas.append)
  self.assertEqual(deltas,['good']);self.assertEqual(calls[1][1]['input'][1]['type'],'image');self.assertEqual(result['status'],'completed')
if __name__=='__main__':unittest.main()
