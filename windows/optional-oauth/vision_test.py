"""Windows SIWC proof of concept. Never reads Codex authentication files."""
import argparse
import base64
import ctypes
from ctypes import wintypes
import hashlib
import json
import os
from pathlib import Path
import queue
import secrets
import shutil
import subprocess
import sys
import threading
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid
import webbrowser
from http.server import BaseHTTPRequestHandler, HTTPServer

APP = 'codex_vision_local_test'
ROOT = Path(__file__).resolve().parent
STATE = ROOT / '.private'
REPORTS = ROOT / 'reports'
ISSUER = 'https://auth.openai.com'
RESOURCE = 'https://api.openai.com/v1'
SCOPE = 'openid profile email offline_access resource.invoke chatgpt.tokens.use.direct'
SECRETS = []


def open_browser(url):
    candidates = [Path(os.environ.get('PROGRAMFILES', 'C:/Program Files')) / 'Google/Chrome/Application/chrome.exe',
                  Path(os.environ.get('PROGRAMFILES(X86)', 'C:/Program Files (x86)')) / 'Google/Chrome/Application/chrome.exe',
                  Path(os.environ.get('LOCALAPPDATA', '')) / 'Google/Chrome/Application/chrome.exe']
    chrome = next((p for p in candidates if p.is_file()), None)
    if chrome is None:
        raise RuntimeError('Google Chrome was not found. No other browser will be opened.')
    webbrowser.BackgroundBrowser(str(chrome)).open(url)


def clean(value):
    text = str(value)
    for secret in SECRETS:
        if secret:
            text = text.replace(secret, '[REDACTED]')
    return text


def save(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_suffix(path.suffix + '.tmp')
    tmp.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding='utf-8')
    tmp.replace(path)


def http_json(url, form=None, token=None):
    headers = {'Accept': 'application/json'}
    if token:
        headers['Authorization'] = 'Bearer ' + token
    data = None
    if form is not None:
        data = urllib.parse.urlencode(form).encode()
        headers['Content-Type'] = 'application/x-www-form-urlencoded'
    request = urllib.request.Request(url, data=data, headers=headers)
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            return json.load(response)
    except urllib.error.HTTPError as e:
        body = e.read(8192).decode('utf-8', errors='replace')
        raise RuntimeError(clean(f'HTTP {e.code}; request_id={e.headers.get("x-request-id")}; {body}')) from None


def protect(data, decrypt=False):
    if os.name != 'nt':
        raise RuntimeError('This credential store requires Windows DPAPI.')
    class Blob(ctypes.Structure):
        _fields_ = [('size', wintypes.DWORD), ('data', ctypes.POINTER(ctypes.c_ubyte))]
    buf = ctypes.create_string_buffer(data)
    src = Blob(len(data), ctypes.cast(buf, ctypes.POINTER(ctypes.c_ubyte)))
    dst = Blob()
    dll = ctypes.WinDLL('crypt32', use_last_error=True)
    fn = dll.CryptUnprotectData if decrypt else dll.CryptProtectData
    fn.argtypes = [ctypes.POINTER(Blob), ctypes.c_void_p, ctypes.c_void_p,
                   ctypes.c_void_p, ctypes.c_void_p, wintypes.DWORD, ctypes.POINTER(Blob)]
    fn.restype = wintypes.BOOL
    if not fn(ctypes.byref(src), None, None, None, None, 1, ctypes.byref(dst)):
        raise ctypes.WinError(ctypes.get_last_error())
    try:
        return ctypes.string_at(dst.data, dst.size)
    finally:
        free = ctypes.windll.kernel32.LocalFree
        free.argtypes = [ctypes.c_void_p]
        free.restype = ctypes.c_void_p
        free(dst.data)


def store_tokens(record):
    STATE.mkdir(parents=True, exist_ok=True)
    target = STATE / 'oauth.dpapi'
    temp = STATE / 'oauth.tmp'
    temp.write_bytes(protect(json.dumps(record).encode()))
    temp.replace(target)


def load_tokens():
    target = STATE / 'oauth.dpapi'
    if not target.exists():
        raise RuntimeError('No independent OAuth grant. Run login first.')
    record = json.loads(protect(target.read_bytes(), decrypt=True))
    SECRETS.extend(record.get(k, '') for k in ('access_token', 'refresh_token', 'id_token'))
    return record


def require_scope(record):
    if 'chatgpt.tokens.use.direct' not in record.get('scope', '').split():
        raise RuntimeError('Identity sign-in succeeded, but ChatGPT plan use was not granted. No inference sent.')


def checked_callback(query, state, saved_client=None):
    if any(len(values) != 1 for values in query.values()):
        raise ValueError('Duplicate callback parameters')
    if not secrets.compare_digest(query.get('state', [''])[0], state):
        raise ValueError('OAuth state mismatch')
    if query.get('error'):
        raise RuntimeError('OAuth authorization declined or failed: ' + query['error'][0])
    client = query.get('client_id', [saved_client])[0]
    if not client or client == 'dynamic_agent_client' or (saved_client and client != saved_client):
        raise ValueError('Missing or mismatched issued client_id')
    code = query.get('code', [''])[0]
    if not code:
        raise ValueError('Missing authorization code')
    return client, code


def login(timeout):
    import jwt
    discovery = http_json(ISSUER + '/.well-known/openid-configuration')
    if discovery.get('issuer') != ISSUER:
        raise RuntimeError('Unexpected OIDC issuer')
    meta_path = STATE / 'registration.json'
    meta = json.loads(meta_path.read_text()) if meta_path.exists() else {'host_id': 'urn:uuid:' + str(uuid.uuid4())}
    save(meta_path, meta)
    state, nonce, verifier = (secrets.token_urlsafe(32) for _ in range(3))
    challenge = base64.urlsafe_b64encode(hashlib.sha256(verifier.encode()).digest()).decode().rstrip('=')
    result = {}
    class Handler(BaseHTTPRequestHandler):
        def log_message(self, *args):
            pass  # Never log callback URLs/codes.
        def do_GET(self):
            parsed = urllib.parse.urlparse(self.path)
            if parsed.path != '/auth/callback':
                self.send_error(404)
                return
            try:
                client, code = checked_callback(urllib.parse.parse_qs(parsed.query), state, meta.get('client_id'))
                result.update(client=client, code=code)
                message = b'Authorization received. Return to the terminal for validation.'
                status = 200
            except Exception as e:
                result['error'] = str(e)
                message, status = b'Authorization could not be validated. Return to the terminal.', 400
            self.send_response(status)
            self.send_header('Content-Type', 'text/plain; charset=utf-8')
            self.end_headers()
            self.wfile.write(message)
    with HTTPServer(('127.0.0.1', 0), Handler) as server:
        server.timeout = 1
        redirect = f'http://127.0.0.1:{server.server_port}/auth/callback'
        params = dict(client_id=meta.get('client_id', 'dynamic_agent_client'),
                      ext_agent_host_id=meta['host_id'], response_type='code', redirect_uri=redirect,
                      scope=SCOPE, resource=RESOURCE, state=state, nonce=nonce,
                      code_challenge_method='S256', code_challenge=challenge)
        if not meta.get('client_id'):
            params['agent_name_hint'] = APP
        url = ISSUER + '/api/accounts/authorize?' + urllib.parse.urlencode(params)
        print('Opening official OAuth in your browser. Authorize codex_vision_local_test.', flush=True)
        print('Waiting for browser authorization (no inference will run automatically).', flush=True)
        open_browser(url)
        deadline = time.monotonic() + timeout
        while not result and time.monotonic() < deadline:
            server.handle_request()
    if not result:
        raise TimeoutError('Browser authorization timed out. Run login again.')
    if 'error' in result:
        raise RuntimeError(result['error'])
    meta['client_id'] = result['client']
    save(meta_path, meta)
    SECRETS.append(result['code'])
    tokens = http_json(ISSUER + '/api/accounts/oauth/token', dict(
        grant_type='authorization_code', client_id=result['client'], code=result['code'],
        code_verifier=verifier, redirect_uri=redirect, resource=RESOURCE))
    SECRETS.extend(tokens.get(k, '') for k in ('access_token', 'refresh_token', 'id_token'))
    if discovery['jwks_uri'] != ISSUER + '/.well-known/jwks.json':
        raise RuntimeError('Unexpected JWKS endpoint')
    key = jwt.PyJWKClient(discovery['jwks_uri']).get_signing_key_from_jwt(tokens['id_token'])
    identity = jwt.decode(tokens['id_token'], key.key, algorithms=['RS256'],
                          audience=result['client'], issuer=ISSUER,
                          options={'require': ['exp', 'iat', 'sub', 'iss', 'aud', 'nonce']})
    if not secrets.compare_digest(identity['nonce'], nonce):
        raise RuntimeError('OIDC nonce mismatch')
    if meta.get('subject') and identity['sub'] != meta['subject']:
        raise RuntimeError('Reauthorization returned a different identity')
    meta['subject'] = identity['sub']
    save(meta_path, meta)
    tokens.update(client_id=result['client'], expires_at=time.time() + float(tokens['expires_in']))
    store_tokens(tokens)
    require_scope(tokens)
    print('OAuth validated. ChatGPT plan scope granted. Credentials protected with Windows DPAPI.')


def access_token():
    tokens = load_tokens()
    require_scope(tokens)
    if tokens['expires_at'] < time.time() + 90:
        replacement = http_json(ISSUER + '/api/accounts/oauth/token', dict(
            grant_type='refresh_token', client_id=tokens['client_id'],
            refresh_token=tokens['refresh_token'], resource=RESOURCE))
        SECRETS.extend(replacement.get(k, '') for k in ('access_token', 'refresh_token', 'id_token'))
        tokens.update(replacement)
        tokens['expires_at'] = time.time() + float(replacement['expires_in'])
        store_tokens(tokens)
        require_scope(tokens)
    return tokens['access_token']


def child_environment(token):
    keep = {'SYSTEMROOT', 'WINDIR', 'COMSPEC', 'PATH', 'PATHEXT', 'TEMP', 'TMP',
            'USERPROFILE', 'APPDATA', 'LOCALAPPDATA', 'PROGRAMDATA', 'PROGRAMFILES',
            'PROGRAMFILES(X86)', 'PROCESSOR_ARCHITECTURE', 'NUMBER_OF_PROCESSORS'}
    env = {k: v for k, v in os.environ.items() if k.upper() in keep}
    env['CODEX_HOME'] = str(STATE / 'codex-home')
    if token:
        env['ACCESS_TOKEN'] = token
    return env


class Server:
    def __init__(self, executable, token=None):
        self.events, self.pending, self.serial = queue.Queue(), [], 0
        home, cwd = STATE / 'codex-home', STATE / 'workspace'
        home.mkdir(parents=True, exist_ok=True)
        cwd.mkdir(parents=True, exist_ok=True)
        config = {
            'model_provider': 'openai_chatgpt_plan',
            'model_providers.openai_chatgpt_plan.name': 'ChatGPT plan',
            'model_providers.openai_chatgpt_plan.base_url': RESOURCE,
            'model_providers.openai_chatgpt_plan.env_key': 'ACCESS_TOKEN',
            'model_providers.openai_chatgpt_plan.wire_api': 'responses',
            'model_providers.openai_chatgpt_plan.requires_openai_auth': False,
            'model_providers.openai_chatgpt_plan.supports_websockets': False,
            'cli_auth_credentials_store': 'file', 'web_search': 'disabled',
            'sandbox_mode': 'read-only', 'approval_policy': 'on-request',
            'features.shell_tool': False, 'features.apps': False,
            'features.plugins': False, 'features.remote_plugin': False,
            'features.hooks': False, 'features.multi_agent': False,
            'features.memories': False, 'features.shell_snapshot': False,
            'skills.bundled.enabled': False, 'skills.include_instructions': False,
            'analytics.enabled': False,
        }
        args = [executable, 'app-server', '--listen', 'stdio://']
        for name, value in config.items():
            args.extend(['-c', name + '=' + json.dumps(value)])
        self.proc = subprocess.Popen(args, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                     stderr=subprocess.PIPE, text=True, encoding='utf-8',
                                     env=child_environment(token), cwd=cwd,
                                     creationflags=getattr(subprocess, 'CREATE_NO_WINDOW', 0))
        self.stderr = []
        def read_stdout():
            try:
                for line in self.proc.stdout:
                    try:
                        self.events.put(json.loads(line))
                    except ValueError:
                        self.events.put({'_error': 'Invalid JSON on app-server stdout'})
            finally:
                self.events.put({'_error': 'App-server stdout closed'})
        def read_stderr():
            for line in self.proc.stderr:
                self.stderr.append(clean(line.rstrip()))
                self.stderr[:] = self.stderr[-30:]
        threading.Thread(target=read_stdout, daemon=True).start()
        threading.Thread(target=read_stderr, daemon=True).start()
        try:
            self.rpc('initialize', {'clientInfo': {'name': APP, 'title': 'Local vision test', 'version': '0.1.0'}})
            self.send({'method': 'initialized', 'params': {}})
        except BaseException:
            self.close()
            raise

    def send(self, message):
        self.proc.stdin.write(json.dumps(message, ensure_ascii=False) + '\n')
        self.proc.stdin.flush()

    def receive(self, timeout):
        try:
            event = self.events.get(timeout=max(0.01, timeout))
        except queue.Empty:
            raise TimeoutError('App-server response timed out') from None
        if '_error' in event:
            raise RuntimeError(event['_error'] + '\n' + '\n'.join(self.stderr))
        if 'method' in event and 'id' in event:
            self.send({'id': event['id'], 'error': {'code': -32601, 'message': 'Tools/approvals are not supported by this vision-only client'}})
        return event

    def rpc(self, method, params, timeout=45):
        self.serial += 1
        request_id = self.serial
        self.send({'id': request_id, 'method': method, 'params': params})
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            event = self.receive(deadline - time.monotonic())
            if event.get('id') == request_id and 'method' not in event:
                if 'error' in event:
                    raise RuntimeError(clean(json.dumps(event['error'])))
                return event['result']
            self.pending.append(event)
        raise TimeoutError(method + ' timed out')

    def ask(self, model, question, image=None, timeout=180, image_data=None, on_delta=None):
        thread = self.rpc('thread/start', {'model': model, 'modelProvider': 'openai_chatgpt_plan',
                          'cwd': str(STATE / 'workspace'), 'ephemeral': True,
                          'sandbox': 'read-only', 'approvalPolicy': 'on-request',
                          'baseInstructions': 'Answer the user directly. Do not use tools, commands, browsing, or filesystem search.'})['thread']['id']
        inputs = [{'type': 'text', 'text': question}]
        if image:
            inputs.append({'type': 'localImage', 'path': str(image)})
        if image_data:
            inputs.append({'type': 'image', 'url': image_data})
        turn = self.rpc('turn/start', {'threadId': thread, 'input': inputs})['turn']['id']
        report = {'model': model, 'threadId': thread, 'turnId': turn, 'question': question,
                  'image': str(image) if image else None, 'status': 'inProgress', 'answers': [], 'usage': None}
        deadline = time.monotonic() + timeout
        try:
            while time.monotonic() < deadline:
                event = self.pending.pop(0) if self.pending else self.receive(deadline-time.monotonic())
                p, method = event.get('params', {}), event.get('method')
                if p.get('threadId') not in (None, thread):
                    continue
                if p.get('turnId') not in (None, turn):
                    continue
                if method == 'item/agentMessage/delta':
                    if on_delta:
                        on_delta(p.get('delta', ''))
                    else:
                        print(p.get('delta', ''), end='', flush=True)
                elif method == 'item/completed' and p.get('item', {}).get('type') == 'agentMessage':
                    report['answers'].append(p['item'].get('text', ''))
                elif method == 'thread/tokenUsage/updated':
                    report['usage'] = p.get('tokenUsage')
                elif method == 'turn/completed' and p.get('turn', {}).get('id') == turn:
                    report['status'] = p['turn']['status']
                    report['error'] = p['turn'].get('error')
                    if not on_delta:
                        print()
                    return report
            raise TimeoutError('Turn timed out')
        except (TimeoutError, KeyboardInterrupt):
            self.send({'id': 99999, 'method': 'turn/interrupt', 'params': {'threadId': thread, 'turnId': turn}})
            raise

    def close(self):
        self.proc.terminate()
        try:
            self.proc.wait(timeout=5)
        except subprocess.TimeoutExpired:
            self.proc.kill()
            self.proc.wait()


def validate_png(path):
    from PIL import Image
    path = Path(path).resolve(strict=True)
    with Image.open(path) as im:
        if im.format != 'PNG':
            raise ValueError('Input must be a PNG file')
        im.verify()
    return path


def fixture():
    from PIL import Image, ImageDraw, ImageFont
    REPORTS.mkdir(parents=True, exist_ok=True)
    code = secrets.token_hex(4).upper()
    im = Image.new('RGB', (700, 300), 'white')
    draw = ImageDraw.Draw(im)
    font = ImageFont.truetype('C:/Windows/Fonts/arial.ttf', 52)
    draw.text((30, 30), code, fill='black', font=font)
    draw.rectangle((40, 150, 150, 260), fill='red')
    draw.ellipse((250, 150, 360, 260), fill='blue')
    path = REPORTS / 'test.png'
    im.save(path)
    return path, code


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--codex', default=shutil.which('codex') or str(Path(os.environ.get('LOCALAPPDATA', '')) / 'OpenAI/Codex/bin/6257d9e8f63c0f2b/codex.exe'))
    sub = parser.add_subparsers(dest='command', required=True)
    sub.add_parser('login').add_argument('--timeout', type=int, default=900)
    sub.add_parser('models')
    sub.add_parser('probe')
    sub.add_parser('usage')
    for command in ('verify', 'ask'):
        p = sub.add_parser(command)
        p.add_argument('--model', required=True)
        p.add_argument('--usage-reviewed', action='store_true', help='Confirm plan/credits settings reviewed in ChatGPT Usage')
        if command == 'ask':
            p.add_argument('--question', required=True)
            p.add_argument('--image')
    args = parser.parse_args()
    if args.command == 'login':
        login(args.timeout)
    elif args.command == 'usage':
        open_browser('https://chatgpt.com/settings/usage')
    elif args.command == 'models':
        catalog = http_json(RESOURCE + '/models', token=access_token())
        save(REPORTS / 'models.json', catalog)
        for m in catalog.get('models', []):
            if m.get('visibility') == 'list':
                print(m.get('slug'), m.get('display_name'))
    elif args.command == 'probe':
        server = Server(args.codex)
        try:
            print('PASS: isolated app-server initialize/initialized; no OAuth or inference used.')
        finally:
            server.close()
    else:
        if not args.usage_reviewed:
            raise RuntimeError('Review ChatGPT Settings > Usage plan/credits settings, then pass --usage-reviewed. No inference sent.')
        image = validate_png(args.image) if args.command == 'ask' and args.image else None
        token = access_token()
        server = Server(args.codex, token)
        results = {'version': subprocess.check_output([args.codex, '--version'], text=True).strip(),
                   'time_utc': time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime()),
                   'auth': 'independent_SIWC_OAuth', 'billing_attribution': 'Requires ChatGPT Usage reconciliation', 'tests': []}
        try:
            if args.command == 'verify':
                text = server.ask(args.model, 'Reply with exactly TEXT_OK.')
                text['content_check'] = any(a.strip() == 'TEXT_OK' for a in text['answers'])
                results['tests'].append(text)
                if text['status'] != 'completed' or not text['content_check']:
                    raise RuntimeError('Text validation failed; image test not sent')
                image, expected = fixture()
                result = server.ask(args.model, 'Read the alphanumeric code in the image, and describe the colors and shapes. Do not use tools.', image)
                result['expected_code'] = expected
                result['code_check'] = any(expected in a.upper() for a in result['answers'])
                results['tests'].append(result)
            else:
                results['tests'].append(server.ask(args.model, args.question, image))
        except BaseException as exc:
            results['client_error'] = clean(exc)
            raise
        finally:
            save(REPORTS / 'last-run.json', json.loads(clean(json.dumps(results))))
            server.close()
        if any(t['status'] != 'completed' or t.get('code_check') is False for t in results['tests']):
            raise RuntimeError('Verification failed; inspect reports/last-run.json')
        print('Saved reports/last-run.json. Reconcile actual plan/credits usage in ChatGPT Settings > Usage.')


if __name__ == '__main__':
    if hasattr(sys.stdout, 'reconfigure'):
        sys.stdout.reconfigure(encoding='utf-8')
    try:
        main()
    except (Exception, KeyboardInterrupt) as exc:
        print('ERROR: ' + clean(exc), file=sys.stderr)
        sys.exit(1)
