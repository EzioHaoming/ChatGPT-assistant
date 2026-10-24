"""Independent macOS Sign in with ChatGPT. Credentials remain in native Keychain."""
import argparse
import base64
import ctypes

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

APP = 'ChatGPT assistant'
ROOT = Path(__file__).resolve().parent
STATE = Path.home() / 'Library/Application Support/ChatGPT assistant/oauth'
REPORTS = ROOT / 'reports'
ISSUER = 'https://auth.openai.com'
RESOURCE = 'https://api.openai.com/v1'
SCOPE = 'openid profile email offline_access resource.invoke chatgpt.tokens.use.direct'
SECRETS = []


def open_browser(url):
    subprocess.run(['/usr/bin/open', url], check=True)


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


def credential_store():
    # Explicit native backend: never silently fall back to a plaintext keyring.
    from keyring.backends.macOS import Keyring
    return Keyring()


def store_tokens(record):
    credential_store().set_password('ChatGPT-assistant-independent-oauth', 'current', json.dumps(record))


def load_tokens():
    value = credential_store().get_password('ChatGPT-assistant-independent-oauth', 'current')
    if not value:
        raise RuntimeError('No independent OAuth grant. Run login first.')
    record = json.loads(value)
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
                message = b'Authorization received. Return to ChatGPT assistant for validation.'
                status = 200
            except Exception as e:
                result['error'] = str(e)
                message, status = b'Authorization could not be validated. Return to ChatGPT assistant.', 400
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
        print('Opening official OAuth in your browser. Authorize ChatGPT assistant.', flush=True)
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
    print('OAuth validated. ChatGPT plan scope granted. Credentials protected with macOS Keychain.')


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

