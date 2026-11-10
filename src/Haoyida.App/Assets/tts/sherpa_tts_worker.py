"""耗易达 TTS sidecar：常驻进程，按行读取 JSON 请求并返回 JSON 响应。

请求： {"text": "...", "sid": 0, "speed": 1.0, "out": "C:/path/out.wav"}
响应： {"ok": true, "out": "...", "sr": 22050} 或 {"ok": false, "error": "..."}

模型由命令行参数指定（sherpa-onnx VITS 目录）。
"""
import json
import os
import sys
import traceback

import numpy as np
import sherpa_onnx


def build_tts(model_dir, threads=2):
    def pick(*names):
        for n in names:
            p = os.path.join(model_dir, n)
            if os.path.exists(p):
                return p
        return ""

    model = pick("model.onnx", "model.fp16.onnx")
    if not model:
        cands = sorted(__import__("glob").glob(os.path.join(model_dir, "*.onnx")))
        cands.sort(key=lambda p: ("int8" in p, len(p)))
        model = cands[0] if cands else ""
    tokens = pick("tokens.txt")
    lexicon = pick("lexicon.txt")
    data_dir = pick("espeak-ng-data")
    dict_dir = pick("dict")

    vits = sherpa_onnx.OfflineTtsVitsModelConfig(
        model=model, lexicon=lexicon, tokens=tokens,
        data_dir=data_dir, dict_dir=dict_dir,
    )
    cfg = sherpa_onnx.OfflineTtsConfig(
        model=sherpa_onnx.OfflineTtsModelConfig(vits=vits, num_threads=threads, provider="cpu"),
        max_num_sentences=1,
    )
    return sherpa_onnx.OfflineTts(cfg)


def write_wav(path, samples, sr):
    import wave
    pcm = (np.clip(samples, -1.0, 1.0) * 32767).astype("<i2")
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(sr)
        w.writeframes(pcm.tobytes())


def main():
    model_dir = sys.argv[1]
    tts = build_tts(model_dir)
    sys.stdout.write(json.dumps({"ok": True, "ready": True, "sample_rate": tts.sample_rate}) + "\n")
    sys.stdout.flush()

    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        try:
            req = json.loads(line)
            text = req["text"]
            sid = int(req.get("sid", 0))
            speed = float(req.get("speed", 1.0))
            out = req["out"]

            audio = tts.generate(text, sid=sid, speed=speed)
            if audio is None or len(audio.samples) == 0:
                raise RuntimeError("合成结果为空")

            write_wav(out, audio.samples, audio.sample_rate)
            resp = {"ok": True, "out": out, "sr": audio.sample_rate, "n": len(audio.samples)}
        except Exception as ex:  # 任何异常都回一行，避免调用方等死
            resp = {"ok": False, "error": f"{type(ex).__name__}: {ex}"}

        sys.stdout.write(json.dumps(resp, ensure_ascii=False) + "\n")
        sys.stdout.flush()


if __name__ == "__main__":
    try:
        main()
    except Exception:
        traceback.print_exc()
        sys.exit(1)
