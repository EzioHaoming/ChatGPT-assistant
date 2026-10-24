"""One request per process; stdin/stdout JSON, no content logs or screenshot files."""
import base64
import io
import json
import sys
from pathlib import Path
from PIL import Image
import oauth_core as core
import api_backend
import responses_backend

def emit(kind, **values):
    print(json.dumps(dict(type=kind, **values), ensure_ascii=False), flush=True)

def image_models(catalog):
    return list(dict.fromkeys(m['slug'] for m in catalog.get('models', [])
                if m.get('visibility') == 'list' and m.get('supported_in_api') is True
                and 'image' in m.get('input_modalities', []) and isinstance(m.get('slug'), str)))

def model_entries(catalog):
    result = {}
    for m in catalog.get('models', []):
        if not isinstance(m, dict) or m.get('visibility') != 'list' or m.get('supported_in_api') is not True:
            continue
        name = m.get('slug')
        if not isinstance(name, str) or not name:
            continue
        modes = m.get('input_modalities')
        result[name] = {'id': name, 'image': ('image' in modes) if isinstance(modes, list) else None}
    return list(result.values())

def selected_model(request):
    import re
    model = request.get('model', 'gpt-6-luna')
    if not isinstance(model, str) or not re.fullmatch(r'[a-zA-Z0-9][a-zA-Z0-9._:/-]{0,255}', model):
        raise ValueError('Invalid model selection')
    return model

def validate_request(request):
    question = request.get('question', '').strip()
    if not question or len(question) > 12000:
        raise ValueError('问题不能为空，且不能超过 12000 字符。')
    encoded = request.get('png', '')
    if encoded == '':
        return question, None
    if len(encoded) > 28_000_000:
        raise ValueError('截图超过 20 MB，请缩小选区。')
    raw = base64.b64decode(encoded, validate=True)
    if len(raw) > 20_000_000:
        raise ValueError('截图超过 20 MB，请缩小选区。')
    with Image.open(io.BytesIO(raw)) as im:
        if im.format != 'PNG' or im.width * im.height > 25_000_000:
            raise ValueError('需要不超过 2500 万像素的 PNG。')
        im.verify()
    return question, 'data:image/png;base64,' + encoded

def main():
    request = json.loads(sys.stdin.readline(28_100_000))
    action = request.get('action')
    if action == 'configure':
        try:
            api_backend.configure(request)
        except Exception:
            emit('error', message='设置未保存：请检查地址、模型 ID、Key 和钥匙串访问权限。')
            return
        emit('done', answer='设置已保存。', usage=None)
        return
    if action == 'api_models':
        try:
            rows = api_backend.models(request)
            emit('done', answer=f'已获取 {len(rows)} 个模型，也可手动输入 ID。', api_models=rows)
        except Exception:
            emit('error', message='刷新失败：请检查服务地址、API Key、网络和模型列表权限；也可手动输入模型 ID。')
        return
    config = api_backend.settings()
    if config.get('api') and action == 'ask':
        question, data = validate_request(request)
        answer = api_backend.ask(config, question, data)
        emit('done', answer=answer, usage=None)
        return
    if action == 'select_model':
        config['model'] = selected_model(request)
        api_backend.configure(config)
        emit('done', answer='模型选择已保存。')
        return
    if action == 'models':
        if config.get('api'):
            rows = api_backend.models(config)
            note = 'API 模型目录已刷新。'
        else:
            catalog = core.http_json(core.RESOURCE + '/models', token=core.access_token())
            rows = model_entries(catalog)
            if not rows:
                raise ValueError('服务未返回可选择模型；保留当前选择。')
            note = '模型目录已刷新；实际调用仍受账号权限和限额约束。'
        emit('done', answer=note, models=rows)
        return
    if request.get('action') == 'usage':
        core.open_browser('https://chatgpt.com/settings/usage')
        emit('done', answer='', usage=None)
        return
    if request.get('action') == 'login':
        # Core login writes status text only; never emit URLs/tokens.
        import contextlib
        with contextlib.redirect_stdout(io.StringIO()):
            core.login(900)
        config['api'] = False
        config['model'] = selected_model(request)
        rows = []
        try:
            rows = model_entries(core.http_json(core.RESOURCE + '/models', token=core.access_token()))
            if rows and config['model'] not in [row['id'] for row in rows]:
                config['model'] = rows[0]['id']
        except Exception:
            pass  # Authorization remains valid; the user can refresh the catalog later.
        api_backend.configure(config)
        emit('done', answer='授权已完成，已启用 ChatGPT。' if rows else '授权已完成，请点击刷新模型获取可用列表。', usage=None, models=rows)
        return
    question, data = validate_request(request)
    token = core.access_token()
    result = responses_backend.ask(token, selected_model({'model': config.get('model', 'gpt-6-luna')}), question, data,
                                   on_delta=lambda delta: emit('delta', text=delta))
    emit('done', answer=result['answer'], usage=result['usage'])

if __name__ == '__main__':
    import signal
    def interrupted(*_):
        raise SystemExit(130)
    signal.signal(signal.SIGTERM, interrupted)
    sys.stdout.reconfigure(encoding='utf-8')
    sys.stdin.reconfigure(encoding='utf-8')
    try:
        main()
    except Exception as exc:
        import traceback
        frames = traceback.extract_tb(exc.__traceback__)
        diagnostic = type(exc).__name__ + ':' + '/'.join(f.name for f in frames[-3:])
        # Never forward raw server exceptions: they can include request content.
        message = str(exc).lower()
        if isinstance(exc, TimeoutError):
            error = '请求超时，可重试；已提交的请求可能计入用量。'
        elif any(s in message for s in ('401', '403', 'oauth', 'grant', 'scope', 'token')):
            error = '授权失效或权限不足，请点击 ChatGPT 授权后重试。'
        elif any(s in message for s in ('429', 'quota', 'limit', 'rate')):
            error = '服务限额或请求频率受限，请在 服务商用量页面核对。'
        elif any(s in message for s in ('model', 'unsupported', 'not supported')):
            error = '所选模型不可用或不支持此请求，请刷新模型列表并手动选择；未自动切换模型。'
        elif isinstance(exc, (ValueError, Image.DecompressionBombError)):
            error = '图片或问题格式无效、过大，请缩小选区并检查问题。'
        else:
            error = '请求失败（网络、会话或服务错误），请重试；未生成有效回答。诊断：' + diagnostic
        emit('error', message=error)
        sys.exit(1)
