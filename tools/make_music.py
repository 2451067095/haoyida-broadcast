"""生成原创背景音乐（无第三方采样，纯合成）。
输出 22050Hz / 16bit / 单声道 WAV，可无缝循环，约 85 秒一首。
"""
import math
import os
import wave

import numpy as np

SR = 22050
OUT = os.path.join(os.path.dirname(__file__), "..", "src", "Haoyida.App", "Assets", "music")


def midi(n):
    return 440.0 * (2.0 ** ((n - 69) / 12.0))


def synth(freq, dur, amp, kind, sr=SR):
    n = int(dur * sr)
    if n <= 0:
        return np.zeros(0, dtype=np.float32)
    t = np.arange(n, dtype=np.float32) / sr
    w = 2 * math.pi * freq * t

    if kind == "pad":
        attack, release = 0.55, 0.9
        env = np.minimum(t / attack, 1.0)
        tail = np.clip((dur - t) / release, 0.0, 1.0)
        env = env * tail
        sig = np.sin(w) + 0.30 * np.sin(2 * w + 0.3) + 0.10 * np.sin(3 * w + 0.8)
    elif kind == "pluck":
        env = np.exp(-t * 3.6) * np.minimum(t / 0.008, 1.0)
        sig = np.sin(w) + 0.28 * np.sin(2 * w) + 0.08 * np.sin(3 * w)
    elif kind == "bass":
        attack, release = 0.03, 0.35
        env = np.minimum(t / attack, 1.0) * np.clip((dur - t) / release, 0.0, 1.0)
        sig = np.sin(w) + 0.18 * np.sin(2 * w)
    elif kind == "bell":
        env = np.exp(-t * 2.2) * np.minimum(t / 0.004, 1.0)
        sig = np.sin(w) + 0.5 * np.sin(2.76 * w) + 0.25 * np.sin(5.4 * w)
    else:
        env = np.ones(n, dtype=np.float32)
        sig = np.sin(w)

    return (sig * env * amp).astype(np.float32)


def add(buf, pos, sig):
    if pos >= len(buf):
        return
    end = min(pos + len(sig), len(buf))
    buf[pos:end] += sig[: end - pos]


def reverb(x, wet=0.22):
    out = x.copy()
    for delay, gain in ((0.031, 0.55), (0.047, 0.42), (0.071, 0.30), (0.113, 0.20)):
        n = int(delay * SR)
        y = np.zeros_like(x)
        y[n:] = x[:-n] * gain
        out += y
    return out * (1.0 - wet) + x * wet


def fade(x, fin=0.6, fout=1.2):
    n_in, n_out = int(fin * SR), int(fout * SR)
    if n_in:
        x[:n_in] *= np.linspace(0, 1, n_in, dtype=np.float32)
    if n_out:
        x[-n_out:] *= np.linspace(1, 0, n_out, dtype=np.float32)
    return x


def master(x, peak=0.72):
    x = np.tanh(x * 1.15) / math.tanh(1.15)
    m = float(np.max(np.abs(x))) or 1.0
    return (x / m * peak).astype(np.float32)


def write_wav(path, x):
    pcm = np.clip(x, -1.0, 1.0)
    pcm = (pcm * 32767.0).astype("<i2")
    with wave.open(path, "wb") as f:
        f.setnchannels(1)
        f.setsampwidth(2)
        f.setframerate(SR)
        f.writeframes(pcm.tobytes())


def compose(name, bpm, chords, bars, arp_octave=1, bell=True, pluck_gain=0.16):
    beat = 60.0 / bpm
    bar = beat * 4
    total = int(bar * bars * SR) + SR
    buf = np.zeros(total, dtype=np.float32)

    for b in range(bars):
        root, third, fifth = chords[b % len(chords)]
        start = int(b * bar * SR)

        # 铺底和声（整小节）
        for note in (root + 12, third + 12, fifth + 12):
            add(buf, start, synth(midi(note), bar * 0.98, 0.085, "pad"))
        # 低音
        add(buf, start, synth(midi(root - 12), beat * 1.6, 0.20, "bass"))
        add(buf, start + int(beat * 2 * SR), synth(midi(root - 12), beat * 1.4, 0.15, "bass"))

        # 琶音：八分音符
        pattern = [root + 12 * arp_octave, fifth + 12 * arp_octave, third + 12 * arp_octave,
                   fifth + 12 * arp_octave, root + 24, fifth + 12 * arp_octave,
                   third + 12 * arp_octave, fifth + 12 * arp_octave]
        for i, note in enumerate(pattern):
            pos = start + int(i * beat * 0.5 * SR)
            gain = pluck_gain * (0.9 if i % 2 == 0 else 0.65)
            add(buf, pos, synth(midi(note), beat * 0.9, gain, "pluck"))

        # 点缀铃音（每两小节一次）
        if bell and b % 2 == 1:
            add(buf, start + int(beat * 3 * SR), synth(midi(fifth + 24), beat * 1.2, 0.075, "bell"))

    x = reverb(buf, 0.20)
    x = fade(x, 0.5, 1.4)
    x = master(x)
    path = os.path.join(OUT, name + ".wav")
    write_wav(path, x)
    print(f"{name}: {len(x)/SR:.1f}s  {os.path.getsize(path)/1024/1024:.1f} MB")


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)

    # C 大调 I–V–vi–IV，明亮
    compose("morning", 92, [(60, 64, 67), (55, 59, 62), (57, 60, 64), (53, 57, 60)], 24,
            arp_octave=1, bell=True, pluck_gain=0.17)

    # F 大调，舒缓
    compose("midday", 78, [(53, 57, 60), (48, 52, 55), (50, 53, 57), (46, 50, 53)], 20,
            arp_octave=0, bell=True, pluck_gain=0.13)

    # A 小调，温暖
    compose("evening", 66, [(57, 60, 64), (53, 57, 60), (48, 52, 55), (55, 59, 62)], 16,
            arp_octave=0, bell=False, pluck_gain=0.11)
    # 以下为扩充曲库：喜庆 / 欢快 / 轻松 / 温馨
    compose("festive", 104, [(62, 66, 69), (55, 59, 62), (57, 60, 64), (50, 53, 57)], 20,
            arp_octave=1, bell=True, pluck_gain=0.19)

    compose("upbeat", 112, [(60, 64, 67), (57, 60, 64), (53, 57, 60), (55, 59, 62)], 24,
            arp_octave=1, bell=False, pluck_gain=0.18)

    compose("relax", 72, [(57, 60, 64), (50, 53, 57), (55, 59, 62), (48, 52, 55)], 18,
            arp_octave=0, bell=True, pluck_gain=0.10)

    compose("warm", 68, [(53, 57, 60), (48, 52, 55), (55, 59, 62), (50, 53, 57)], 16,
            arp_octave=0, bell=True, pluck_gain=0.11)
