import hashlib
import ipaddress
import json
import urllib.parse
import urllib.request
from pathlib import Path

FOLDER = Path.home() / 'Library/Application Support/ChatGPT assistant'

def settings():
    path = FOLDER / 'settings.json'
    c = json.loads(path.read_text()) if path.exists() else {}
    c.setdefault('chatgpt_model', c.get('model', 'gpt-6-luna') if not c.get('api') else 'gpt-6-luna')
    c.setdefault('api_model', c.get('model', '') if c.get('api') else '')
    c.setdefault('provider', 'custom')
    c['model'] = c['api_model'] if c.get('api') else c['chatgpt_model']
    return c

def endpoint(value):
    p = urllib.parse.urlsplit(value.strip().rstrip('/'))
    local = p.hostname == 'localhost'
    try:
        local = local or ipaddress.ip_address(p.hostname or '').is_loopback
    except ValueError:
        pass
    if p.scheme != 'https' and not (p.scheme == 'http' and local):
        raise ValueError('HTTPS required')
    if not p.hostname or p.username or p.password or p.query or p.fragment or '{' in value or '}' in value:
        raise ValueError('Invalid endpoint')
    if p.path.endswith('/chat/completions'):
        raise ValueError('Use Base URL without chat/completions')
    return value.strip().rstrip('/') + '/chat/completions'

def key_id(url):
    return hashlib.sha256(endpoint(url).encode()).hexdigest()

def configure(request):
    from oauth_core import credential_store
    c = settings().copy()
    c.update({k: request[k] for k in ('url', 'model', 'provider') if k in request})
    c.setdefault('url', '')
    c['api'] = bool(request.get('api'))
    c['url'] = c['url'].strip().rstrip('/')
    c['model'] = c['model'].strip()
    if c['url']:
        endpoint(c['url'])
    if not c['model']:
        raise ValueError('Model required')
    if c['api'] or request.get('key'):
        kid = key_id(c['url'])
        store = credential_store()
        if request.get('key'):
            store.set_password('ChatGPT-assistant-custom-api', kid, request['key'].strip())
        if c['api'] and not store.get_password('ChatGPT-assistant-custom-api', kid):
            raise ValueError('API key required')
    c.pop('codex', None)
    c['api_model' if c['api'] else 'chatgpt_model'] = c['model']
    FOLDER.mkdir(parents=True, exist_ok=True, mode=0o700)
    temporary = FOLDER / 'settings.tmp'
    temporary.write_text(json.dumps(c), encoding='utf-8')
    temporary.chmod(0o600)
    temporary.replace(FOLDER / 'settings.json')

def payload(model, question, image):
    content = [{'type': 'text', 'text': question}]
    if image:
        content.append({'type': 'image_url', 'image_url': {'url': image}})
    return {'model': model, 'stream': False, 'messages': [{'role': 'user', 'content': content}]}

class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs):
        return None

def ask(config, question, image):
    from oauth_core import credential_store
    key = credential_store().get_password('ChatGPT-assistant-custom-api', key_id(config['url']))
    if not key:
        raise ValueError('API key missing')
    request = urllib.request.Request(endpoint(config['url']), data=json.dumps(payload(config['model'], question, image)).encode(),
        headers={'Content-Type': 'application/json', 'Authorization': 'Bearer ' + key})
    with urllib.request.build_opener(NoRedirect()).open(request, timeout=180) as response:
        raw = response.read(8_000_001)
    if len(raw) > 8_000_000:
        raise ValueError('Response too large')
    result = json.loads(raw)['choices'][0]
    if result.get('finish_reason') in ('length', 'content_filter'):
        raise RuntimeError('Response incomplete')
    answer = result['message']['content']
    if not isinstance(answer, str) or not answer.strip():
        raise ValueError('Empty response')
    return answer


def models(config):
    """Read a draft endpoint's catalog without saving settings or credentials."""
    from oauth_core import credential_store
    base = endpoint(config.get('url', '')).removesuffix('/chat/completions')
    key = config.get('key', '').strip()
    if not key:
        key = credential_store().get_password('ChatGPT-assistant-custom-api', key_id(base))
    if not key:
        raise ValueError('API key required')
    request = urllib.request.Request(base + '/models', headers={'Authorization': 'Bearer ' + key, 'Accept': 'application/json'})
    with urllib.request.build_opener(NoRedirect()).open(request, timeout=30) as response:
        raw = response.read(2_000_001)
    if len(raw) > 2_000_000:
        raise ValueError('Model catalog too large')
    data = json.loads(raw).get('data')
    if not isinstance(data, list):
        raise ValueError('Invalid model catalog')
    rows = {}
    for item in data:
        name = item.get('id') if isinstance(item, dict) else None
        if isinstance(name, str) and name.strip() and len(name) <= 256:
            modes = item.get('input_modalities')
            rows[name] = {'id': name, 'image': 'image' in modes if isinstance(modes, list) else None}
    if not rows:
        raise ValueError('Empty model catalog')
    return sorted(rows.values(), key=lambda r: r['id'].lower())
