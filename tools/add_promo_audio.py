"""给 9:16 宣传片加口播 + 背景音乐。

1) 用内置 Piper 逐段合成旁白（22050Hz mono）
2) 按场景时间轴拼成旁白轨（不足补静音，长了截断）
3) 混入原创 BGM（低音量铺底）
4) ffmpeg 封装：视频流直拷 + AAC 音频
"""
import os
import subprocess
import wave

import imageio_ffmpeg
import numpy as np

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
DIST = r"C:\Haoyida"  # 用 ASCII 路径下的运行时，piper 不认中文路径
PIPER = os.path.join(DIST, "piper", "piper.exe")
VOICE = os.path.join(DIST, "piper", "voices", "zh_CN-huayan-medium.onnx")
BGM = os.path.join(DIST, "Assets", "music", "upbeat.wav")
VIDEO_IN = os.path.join(ROOT, "marketing", "耗易达-功能演示-9x16.mp4")
VIDEO_OUT = os.path.join(ROOT, "marketing", "耗易达-功能演示-9x16-有声.mp4")
TMP = os.path.join(os.environ.get("TEMP", r"C:\Temp"), "hyd_audio")

SR = 22050
TOTAL = 37.0

# (起始秒, 文案)
LINES = [
    (0.6, "超市广播，还在靠员工喊吗？"),
    (5.4, "定时播报，到点自动响，设一次，全年不用管。"),
    (11.4, "促销商品，一键生成播报词，价格读成六块九毛八。"),
    (17.4, "一百个中文音色，男声女声，完全离线运行。"),
    (23.4, "分时段背景音乐，人声一来，自动压低。"),
    (29.4, "播了什么，一清二楚。"),
    (33.4, "关注微信公众号，获取门店播报文案模板。"),
]


def synth(text, path):
    p = subprocess.run([PIPER, "--model", VOICE, "--output_file", path, "--length_scale", "1.05"],
                       input=text.encode("utf-8"), capture_output=True, cwd=os.path.dirname(PIPER), timeout=120)
    if p.returncode != 0 or not os.path.exists(path) or os.path.getsize(path) < 1000:
        raise RuntimeError(f"piper 合成失败: {p.stderr.decode('utf-8', 'ignore')[-200:]}")


def read_wav(path):
    with wave.open(path, "rb") as w:
        assert w.getsampwidth() == 2 and w.getnchannels() == 1
        sr = w.getframerate()
        data = np.frombuffer(w.readframes(w.getnframes()), dtype="<i2").astype(np.float32) / 32768.0
    if sr != SR:
        # 简单线性重采样到 22050
        n = int(len(data) * SR / sr)
        data = np.interp(np.linspace(0, len(data) - 1, n), np.arange(len(data)), data).astype(np.float32)
    return data


def main():
    os.makedirs(TMP, exist_ok=True)
    total_n = int(TOTAL * SR)
    narration = np.zeros(total_n, dtype=np.float32)

    for i, (start, text) in enumerate(LINES):
        p = os.path.join(TMP, f"line{i}.wav")
        synth(text, p)
        seg = read_wav(p)
        at = int(start * SR)
        end = min(at + len(seg), total_n)
        narration[at:end] += seg[: end - at]
        print(f"  line{i}: {len(seg)/SR:.2f}s @{start}s")

    # BGM 铺底：循环 + 淡入淡出
    bgm = read_wav(BGM)
    reps = int(np.ceil(total_n / len(bgm)))
    bgm = np.tile(bgm, reps)[:total_n]
    bgm = bgm / (np.max(np.abs(bgm)) or 1.0) * 0.16
    fade = int(1.2 * SR)
    bgm[:fade] *= np.linspace(0, 1, fade)
    bgm[-fade:] *= np.linspace(1, 0, fade)

    # 旁白比背景高很多；简单限幅防爆音
    mix = narration * 0.95 + bgm
    peak = np.max(np.abs(mix)) or 1.0
    if peak > 0.98:
        mix = mix / peak * 0.98

    mix_path = os.path.join(TMP, "mix.wav")
    with wave.open(mix_path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes((np.clip(mix, -1, 1) * 32767).astype("<i2").tobytes())

    ff = imageio_ffmpeg.get_ffmpeg_exe()
    subprocess.run([ff, "-y", "-i", VIDEO_IN, "-i", mix_path,
                    "-c:v", "copy", "-c:a", "aac", "-b:a", "128k", "-shortest", VIDEO_OUT],
                   check=True, capture_output=True)
    print("done:", VIDEO_OUT, round(os.path.getsize(VIDEO_OUT) / 1048576, 1), "MB")


if __name__ == "__main__":
    main()
