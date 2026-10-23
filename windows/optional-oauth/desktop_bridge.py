"""One request per process; stdin/stdout JSON, no content logs or screenshot files."""
import base64
import io
import json
import sys
from pathlib import Path
from PIL import Image
import vision_test as core

def emit(kind, **values):
    print(json.dumps(dict(type=kind, **values), ensure_ascii=False), flush=True)

def image_models(catalog):
    return list(dict.fromkeys(m['slug'] for m in catalog.get('models', [])
                if m.get('visibility') == 'list' and m.get('supported_in_api') is True
                and 'image' in m.get('input_modalities', []) and isinstance(m.get('slug'), str)))

def selected_model(request):
    import re
    model = request.get('model', 'gpt-6-luna')
    if not isinstance(model, str) or not re.fullmatch(r'[a-zA-Z0-9][a-zA-Z0-9._:-]{0,127}', model):
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
    if request.get('action') == 'models':
        catalog = core.http_json(core.RESOURCE + '/models', token=core.access_token())
        emit('done', answer=json.dumps(image_models(catalog)), usage=None)
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
        emit('done', answer='独立 OAuth 授权已完成。', usage=None)
        return
    question, data = validate_request(request)
    token = core.access_token()
    server = core.Server(request['codex'], token)
    try:
        result = server.ask(selected_model(request), question, image_data=data,
                            on_delta=lambda delta: emit('delta', text=delta))
        if result['status'] != 'completed':
            raise RuntimeError(str(result.get('error') or result['status']))
        answer = '\n\n'.join(result['answers'])
        if not answer.strip():
            raise RuntimeError('服务返回空回答。')
        emit('done', answer=answer, usage=result['usage'])
    finally:
        server.close()

if __name__ == '__main__':
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
            error = '授权失效或权限不足，请点击 Chrome 授权后重试。'
        elif any(s in message for s in ('429', 'quota', 'limit', 'rate')):
            error = '服务限额或请求频率受限，请在 Chrome 用量页面核对。'
        elif any(s in message for s in ('model', 'unsupported', 'not supported')):
            error = '所选模型不可用或不支持此请求，请刷新模型列表并手动选择；未自动切换模型。'
        elif isinstance(exc, (ValueError, Image.DecompressionBombError)):
            error = '图片或问题格式无效、过大，请缩小选区并检查问题。'
        else:
            error = '请求失败（网络、会话或服务错误），请重试；未生成有效回答。诊断：' + diagnostic
        emit('error', message=error)
        sys.exit(1)
