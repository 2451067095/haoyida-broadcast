"""耗易达内置本地 TTS 服务示例（HTTP）。

用法：
    python tools/local_tts_service.py --port 9880 --piper <piper目录> --voice <模型名>

接口：
    GET  /health          -> {"ok": true}
    POST /tts  {"text":"...","voice":"default","speed":1.0} -> audio/wav

把 --piper 换成 CosyVoice / MiMo 的启动脚本，即可用同一套接口接入本软件。
"""
import argparse
import json
import os
import subprocess
import sys
import tempfile

from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

PIPER_DIR = ""
VOICE = ""


def synth(text: str, speed: float) -> bytes:
    """用 Piper 合成一段语音（这是“免费离线”的默认实现）。"""
    exe = os.path.join(PIPER_DIR, "piper.exe")
    model = os.path.join(PIPER_DIR, "voices", f"{VOICE}.onnx")
    if not os.path.exists(exe) or not os.path.exists(model):
        raise RuntimeError("piper 运行时或音色不存在")

    out = os.path.join(tempfile.gettempdir(), "haoyida_http_%d.wav" % os.getpid())
    proc = subprocess.run(
        [exe, "--model", model, "--output_file", out,
         "--length_scale", "%.3f" % (1.0 / max(0.5, speed))],
        input=text.encode("utf-8"), capture_output=True,
        cwd=PIPER_DIR, timeout=90,
    )
    if proc.returncode != 0 or not os.path.exists(out):
        raise RuntimeError("piper 合成失败: " + proc.stderr.decode("utf-8", "ignore")[-200:])

    with open(out, "rb") as f:
        data = f.read()
    try:
        os.remove(out)
    except OSError:
        pass
    return data


class Handler(BaseHTTPRequestHandler):
    def log_message(self, *args):
        pass

    def do_GET(self):
        if self.path.startswith("/health"):
            body = json.dumps({"ok": True, "engine": "piper"}).encode()
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)
            return
        self.send_error(404)

    def do_POST(self):
        if not self.path.startswith("/tts"):
            self.send_error(404)
            return
        try:
            length = int(self.headers.get("Content-Length", "0"))
            req = json.loads(self.rfile.read(length) or b"{}")
            audio = synth(req.get("text", ""), float(req.get("speed", 1.0)))
            self.send_response(200)
            self.send_header("Content-Type", "audio/wav")
            self.send_header("Content-Length", str(len(audio)))
            self.end_headers()
            self.wfile.write(audio)
        except Exception as ex:  # 返回错误文本，方便软件端显示
            body = json.dumps({"error": str(ex)}, ensure_ascii=False).encode()
            self.send_response(500)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)


def main():
    global PIPER_DIR, VOICE
    ap = argparse.ArgumentParser()
    ap.add_argument("--port", type=int, default=9880)
    ap.add_argument("--piper", required=True, help="piper 运行时目录")
    ap.add_argument("--voice", default="zh_CN-huayan-medium")
    args = ap.parse_args()
    PIPER_DIR, VOICE = args.piper, args.voice

    server = ThreadingHTTPServer(("127.0.0.1", args.port), Handler)
    print(f"listening on http://127.0.0.1:{args.port}", flush=True)
    server.serve_forever()


if __name__ == "__main__":
    main()
