"""耗易达 9:16 功能演示短片（1080x1920 / 24fps / 约 36s）。
纯 Python 渲染帧 + imageio-ffmpeg 编码，不依赖 Node。
"""
import os
import subprocess
import tempfile

from PIL import Image, ImageDraw, ImageFont, ImageFilter
import imageio_ffmpeg

W, H, FPS = 1080, 1920, 24
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
SHOTS = os.path.join(ROOT, "docs", "screenshots")
ASSETS = os.path.join(ROOT, "src", "Haoyida.App", "Assets")
OUT = os.path.join(ROOT, "marketing", "耗易达-功能演示-9x16.mp4")

BG = (9, 9, 11)
ACCENT = (59, 158, 255)
TEXT = (250, 250, 250)
DIM = (161, 161, 170)
FAINT = (122, 122, 133)
CARD = (18, 18, 22)
STROKE = (38, 38, 46)

F_BOLD = ImageFont.truetype(r"C:\Windows\Fonts\msyhbd.ttc", 72)
F_TITLE = ImageFont.truetype(r"C:\Windows\Fonts\msyhbd.ttc", 92)
F_SUB = ImageFont.truetype(r"C:\Windows\Fonts\msyh.ttc", 40)
F_SMALL = ImageFont.truetype(r"C:\Windows\Fonts\msyh.ttc", 32)
F_TINY = ImageFont.truetype(r"C:\Windows\Fonts\msyh.ttc", 26)
F_NUM = ImageFont.truetype(r"C:\Windows\Fonts\msyhbd.ttc", 150)

SCENES = [
    {"kind": "hook", "sec": 5,
     "l1": "超市广播", "l2": "还在靠员工喊？",
     "pill": "一次设置 · 全年自动"},
    {"kind": "shot", "sec": 6, "img": "02-plan.png",
     "title": "定时播报", "sub": "每天 / 每周 / 时段循环 · 到点自动响",
     "bullets": ["早间迎宾 07:35", "生鲜轮播 每 45 分钟", "闭店提醒 21:45"]},
    {"kind": "shot", "sec": 6, "img": "04-products.png",
     "title": "促销一键成词", "sub": "价格读成「六块九毛八」，不是机器音",
     "bullets": ["Excel 批量导入", "同名商品自动更新", "立省 / 折扣自动算"]},
    {"kind": "shot", "sec": 6, "img": "06-voices.png",
     "title": "100 个中文音色", "sub": "男声女声、明亮低沉，还能变调",
     "bullets": ["Kokoro 中文多音色", "Piper 神经语音", "完全离线运行"]},
    {"kind": "shot", "sec": 6, "img": "05-music.png",
     "title": "分时段背景音乐", "sub": "人声播报时自动压低，播完恢复",
     "bullets": ["7 首原创曲目", "喜庆 / 欢快 / 轻松 / 温馨", "无版权风险"]},
    {"kind": "shot", "sec": 4, "img": "07-logs.png",
     "title": "播了什么，一清二楚", "sub": "成功、失败、耗时，全部可查可导出",
     "bullets": ["每条都有记录", "失败写清原因", "支持 CSV 导出"]},
    {"kind": "cta", "sec": 4,
     "title": "耗易达 · 超市播音软件",
     "sub": "超市营销好帮手",
     "line": "关注公众号 · 获取播报文案模板"},
]


def load_shot(name, max_w=880):
    p = os.path.join(SHOTS, name)
    if not os.path.exists(p):
        return None
    im = Image.open(p).convert("RGB")
    ratio = max_w / im.width
    return im.resize((max_w, int(im.height * ratio)), Image.LANCZOS)


def rounded(im, radius=26):
    mask = Image.new("L", im.size, 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, im.width, im.height], radius=radius, fill=255)
    out = Image.new("RGBA", im.size, (0, 0, 0, 0))
    out.paste(im, (0, 0), mask)
    return out


def background():
    bg = Image.new("RGB", (W, H), BG)
    d = ImageDraw.Draw(bg)
    # 顶部光晕
    glow = Image.new("RGB", (W, H), BG)
    gd = ImageDraw.Draw(glow)
    gd.ellipse([-300, -520, W + 300, 420], fill=(16, 30, 52))
    glow = glow.filter(ImageFilter.GaussianBlur(120))
    bg = Image.blend(bg, glow, 0.9)
    # 细网格
    for y in range(0, H, 80):
        d.line([(0, y), (W, y)], fill=(14, 14, 18), width=1)
    return bg


BASE = background()


def ease_out(t):
    return 1 - (1 - t) ** 3


def draw_brand(img, d):
    logo_path = os.path.join(ASSETS, "app.png")
    if os.path.exists(logo_path):
        logo = Image.open(logo_path).convert("RGBA").resize((72, 72), Image.LANCZOS)
        img.paste(logo, (76, 92), logo)
    d.text((166, 108), "店铺播音软件", font=F_BOLD, fill=TEXT)
    d.text((166, 108), "", font=F_BOLD, fill=TEXT)
    d.text((166, 108), "", font=F_TINY, fill=DIM)
    d.text((166, 112), "", font=F_TINY, fill=DIM)


def draw_footer(d, idx, total, progress):
    y = H - 150
    d.text((76, y), "超市营销好帮手", font=F_TINY, fill=FAINT)
    # 进度条
    x0, x1 = 76, W - 76
    d.rounded_rectangle([x0, y + 56, x1, y + 64], radius=4, fill=(30, 30, 38))
    w = int((x1 - x0) * progress)
    if w > 0:
        d.rounded_rectangle([x0, y + 56, x0 + w, y + 64], radius=4, fill=ACCENT)


def scene_hook(d, img, s, t):
    a = ease_out(min(1, t / 0.55))
    dy = int(46 * (1 - a))
    d.text((76, 620 - dy), s["l1"], font=F_SUB, fill=DIM)
    d.text((76, 690 - dy), s["l2"], font=F_TITLE, fill=TEXT)
    # 胶囊
    label = s["pill"]
    tw = d.textlength(label, font=F_SMALL)
    d.rounded_rectangle([76, 860 - dy, 76 + tw + 56, 926 - dy], radius=33, outline=(46, 110, 180), width=2,
                        fill=(16, 34, 54))
    d.text((104, 872 - dy), label, font=F_SMALL, fill=ACCENT)
    # 大数字
    d.text((76, 1010), "7", font=F_NUM, fill=ACCENT)
    d.text((190, 1090), "项能力，一次设置", font=F_SUB, fill=DIM)


def scene_shot(d, img, s, t):
    shot = s.get("_shot")
    a = ease_out(min(1, t / 0.5))
    dy = int(50 * (1 - a))

    d.text((76, 250 - dy), s["title"], font=F_BOLD, fill=TEXT)
    d.text((76, 350 - dy), s["sub"], font=F_SMALL, fill=DIM)

    if shot is not None:
        card = rounded(shot)
        x = (W - card.width) // 2
        y = 470 - dy
        shadow = Image.new("RGBA", (card.width + 80, card.height + 80), (0, 0, 0, 0))
        sd = ImageDraw.Draw(shadow)
        sd.rounded_rectangle([40, 46, card.width + 40, card.height + 46], radius=30, fill=(0, 0, 0, 160))
        shadow = shadow.filter(ImageFilter.GaussianBlur(26))
        img.paste(shadow, (x - 40, y - 40), shadow)
        img.paste(card, (x, y), card)
        d.rounded_rectangle([x, y, x + card.width, y + card.height], radius=26, outline=STROKE, width=2)

    by = 470 + (s.get("_shot_h", 380)) + 90
    for i, b in enumerate(s["bullets"]):
        yy = by + i * 74 - dy
        d.ellipse([76, yy + 16, 92, yy + 32], fill=ACCENT)
        d.text((112, yy), b, font=F_SMALL, fill=DIM)


def scene_cta(d, img, s, t):
    a = ease_out(min(1, t / 0.6))
    dy = int(40 * (1 - a))
    d.text((W // 2, 620 - dy), s["title"], font=F_BOLD, fill=TEXT, anchor="mm")
    d.text((W // 2, 730 - dy), s["sub"], font=F_SUB, fill=ACCENT, anchor="mm")

    # 按平台要求不出现二维码，只做文字引导
    d.text((W // 2, 930 - dy), "关注微信公众号", font=F_TITLE, fill=TEXT, anchor="mm")
    d.text((W // 2, 1070 - dy), "获取门店播报文案模板", font=F_SUB, fill=DIM, anchor="mm")
    d.text((W // 2, 1190 - dy), "微信搜索：耗易达", font=F_SMALL, fill=ACCENT, anchor="mm")


def render():
    total_sec = sum(s["sec"] for s in SCENES)
    total_frames = int(total_sec * FPS)

    for s in SCENES:
        if s["kind"] == "shot":
            im = load_shot(s["img"])
            s["_shot"] = rounded(im) if im is not None else None
            s["_shot_h"] = im.height if im is not None else 380

    tmp = tempfile.mkdtemp(prefix="hyd-video-")
    print("frames ->", tmp)

    # 场景起点（秒）
    starts, acc = [], 0.0
    for s in SCENES:
        starts.append(acc)
        acc += s["sec"]

    fi = 0
    for f in range(total_frames):
        sec = f / FPS
        idx = 0
        for i, st in enumerate(starts):
            if sec >= st:
                idx = i
        s = SCENES[idx]
        t = sec - starts[idx]
        progress = (f + 1) / total_frames

        img = BASE.copy()
        d = ImageDraw.Draw(img)
        draw_brand(img, d)
        d = ImageDraw.Draw(img)

        if s["kind"] == "hook":
            scene_hook(d, img, s, t)
        elif s["kind"] == "cta":
            scene_cta(d, img, s, t)
        else:
            scene_shot(d, img, s, t)

        draw_footer(d, idx, len(SCENES), progress)

        # 转场淡入
        fade = min(1.0, t / 0.28)
        if fade < 1.0:
            black = Image.new("RGB", (W, H), (0, 0, 0))
            img = Image.blend(black, img, fade)

        img.save(os.path.join(tmp, f"f{f:05d}.png"))
        fi += 1
        if fi % 120 == 0:
            print(f"  {fi}/{total_frames}")

    ff = imageio_ffmpeg.get_ffmpeg_exe()
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    cmd = [ff, "-y", "-framerate", str(FPS), "-i", os.path.join(tmp, "f%05d.png"),
           "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "20", "-preset", "medium", OUT]
    subprocess.run(cmd, check=True, capture_output=True)
    print("done:", OUT, round(os.path.getsize(OUT) / 1048576, 1), "MB")


if __name__ == "__main__":
    render()
