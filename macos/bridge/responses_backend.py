"""Direct SIWC Responses client: no external CLI, retries, tools or local files."""
import json
import time
import urllib.request
from api_backend import NoRedirect


def payload(model, question, image=None):
    content = [{'type': 'input_text', 'text': question}]
    if image:
        content.append({'type': 'input_image', 'image_url': image})
    return {'model': model, 'input': [{'role': 'user', 'content': content}],
            'store': False, 'stream': True}


def events(response):
    data, size = [], 0
    while True:
        raw = response.readline(8_000_001)
        size += len(raw)
        if size > 16_000_000 or len(raw) > 8_000_000:
            raise RuntimeError('Response too large')
        if not raw:
            if data:
                yield json.loads('\n'.join(data))
            return
        line = raw.decode('utf-8').rstrip('\r\n')
        if not line:
            if data:
                joined = '\n'.join(data)
                if joined == '[DONE]':
                    return
                yield json.loads(joined)
                data = []
        elif line.startswith('data:'):
            data.append(line[5:].lstrip(' '))


def ask(token, model, question, image=None, on_delta=None):
    request = urllib.request.Request('https://api.openai.com/v1/responses',
        data=json.dumps(payload(model, question, image)).encode(),
        headers={'Authorization': 'Bearer ' + token, 'Content-Type': 'application/json',
                 'Accept': 'text/event-stream'})
    chunks = []
    deadline = time.monotonic() + 180
    with urllib.request.build_opener(NoRedirect()).open(request, timeout=180) as response:
        for event in events(response):
            if time.monotonic() > deadline:
                raise TimeoutError('Response timed out')
            kind = event.get('type')
            if kind == 'response.output_text.delta':
                delta = event.get('delta', '')
                chunks.append(delta)
                if on_delta:
                    on_delta(delta)
            if kind in ('response.failed', 'response.incomplete', 'error'):
                error = event.get('response', {}).get('error') or event.get('error') or {}
                code = error.get('code', 'incomplete_response') if isinstance(error, dict) else 'response_error'
                raise RuntimeError(str(code))
            if kind == 'response.completed':
                result = event['response']
                if result.get('status') != 'completed':
                    raise RuntimeError('Response incomplete')
                answer = '\n\n'.join(part.get('text', '') for item in result.get('output', [])
                    if item.get('type') == 'message' for part in item.get('content', [])
                    if part.get('type') == 'output_text')
                if not answer.strip():
                    answer = ''.join(chunks)
                if not answer.strip():
                    raise RuntimeError('Empty response')
                return {'answer': answer, 'usage': result.get('usage')}
    raise RuntimeError('Stream ended without response.completed')
