"""耗易达 Kokoro-zh sidecar：常驻进程，逐行处理 JSON 请求。

请求： {"text": "...", "voice": "zf_001", "speed": 1.0, "out": "C:/x.wav"}
响应： {"ok": true, "sr": 24000, "n": 104400}
"""
import json
import os
import sys
import traceback
import wave

import logging
import jieba

import numpy as np
import onnxruntime as ort
from misaki import zh


def pick_model(d):
    for name in ("model_quantized.onnx", "model_int8.onnx", "model.onnx"):
        p = os.path.join(d, "onnx", name)
        if os.path.exists(p):
            return p
    for root, _, files in os.walk(d):
        for f in files:
            if f.endswith(".onnx"):
                return os.path.join(root, f)
    return ""


def main():
    d = sys.argv[1]
    model = pick_model(d)
    vocab = json.load(open(os.path.join(d, "tokenizer.json"), encoding="utf-8"))["model"]["vocab"]
    jieba.setLogLevel(logging.ERROR)
    g2p = zh.ZHG2P(version="1.1")
    sess = ort.InferenceSession(model, providers=["CPUExecutionProvider"])
    cache = {}

    sys.stdout.write(json.dumps({"ok": True, "ready": True}) + "\n")
    sys.stdout.flush()

    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        try:
            req = json.loads(line)
            text = req["text"]
            voice = req.get("voice", "zf_001")
            speed = float(req.get("speed", 1.0))
            out = req["out"]

            if voice not in cache:
                vp = os.path.join(d, "voices", voice + ".bin")
                cache[voice] = np.fromfile(vp, dtype=np.float32).reshape(510, 256)

            res = g2p(text)
            ph = res[0] if isinstance(res, tuple) else res
            ids = [vocab[c] for c in ph if c in vocab]
            if not ids:
                raise RuntimeError("没有可用音素")

            style = cache[voice][min(len(ids), 509)]
            audio, _ = sess.run(None, {
                "input_ids": np.array([ids], dtype=np.int64),
                "style": style.reshape(1, 256).astype(np.float32),
                "speed": np.array([speed], dtype=np.float32),
            })

            pcm = (np.clip(audio, -1.0, 1.0) * 32767).astype("<i2")
            with wave.open(out, "wb") as w:
                w.setnchannels(1)
                w.setsampwidth(2)
                w.setframerate(24000)
                w.writeframes(pcm.tobytes())

            sys.stdout.write(json.dumps({"ok": True, "sr": 24000, "n": int(audio.size)}) + "\n")
        except Exception as ex:
            sys.stdout.write(json.dumps({"ok": False, "error": f"{type(ex).__name__}: {ex}"}, ensure_ascii=False) + "\n")
        sys.stdout.flush()


if __name__ == "__main__":
    try:
        main()
    except Exception:
        traceback.print_exc()
        sys.exit(1)
