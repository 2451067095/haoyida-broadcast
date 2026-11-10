# 第三方语音模型接入

软件不绑定语音供应商。「音色引擎 → 第三方语音模型 → + 安装模型」可以接入任意本地模型或推理服务，支持两种方式。

## 一、本地命令行（CLI）

适合自带 `run.py` / `tts.exe` 的模型（Piper、GPT-SoVITS、MeloTTS…）。

| 字段 | 说明 |
| --- | --- |
| 命令模板 | 会被 `cmd /c` 执行，支持占位符：`{text}` 文本、`{out}` 输出 wav 路径、`{voice}` 音色、`{speed}` 语速 |
| 工作目录 | 可选；模型脚本需要相对路径时填 |
| 音色列表 | 逗号分隔，会出现在音色下拉里 |

示例：

```
python run_tts.py --text "{text}" --out "{out}" --speaker {voice} --speed {speed}
```

要求：命令执行完，`{out}` 指向的文件必须是有效音频（wav/mp3，≥ 512 字节）。软件会先写到纯 ASCII 暂存区，再搬回缓存目录，避免 C++ 程序不认中文路径。

## 二、HTTP 服务（推荐给大模型）

适合 8B 级、需要显卡的模型（MiMo-V2.5-TTS、CosyVoice、IndexTTS…）。软件 POST JSON，接收音频字节或 `{"audio": "base64"}`。

| 字段 | 说明 |
| --- | --- |
| 接口地址 | 例如 `http://127.0.0.1:9880/tts` |
| 请求体模板 | 默认 `{"text":"{text}","voice":"{voice}","speed":{speed}}` |
| 音色列表 | 与服务的 voice 参数对应 |

### MiMo-V2.5-TTS 的接法（本机有显卡）

MiMo 是小米开源的 8B 语音管线（含 voicedesign），权重与推理需要 GPU，不适合塞进 WPF 进程。做法是**单独起一个服务**，再用本软件接：

```python
# mimo_server.py —— 在本机（有 CUDA）运行：python mimo_server.py
from fastapi import FastAPI, Response
from pydantic import BaseModel
import io, soundfile as sf
# from mimo import MiMoTTS   # 按官方仓库的推理入口替换
app = FastAPI()
tts = None  # MiMoTTS.from_pretrained("XiaomiMiMo/MiMo-V2.5-TTS", device="cuda")

class Req(BaseModel):
    text: str
    voice: str = "default"
    speed: float = 1.0

@app.post("/tts")
def synth(req: Req):
    wav = tts.synthesize(req.text, voice=req.voice, speed=req.speed)  # 按官方 API 调整
    buf = io.BytesIO()
    sf.write(buf, wav, 24000, format="WAV")
    return Response(content=buf.getvalue(), media_type="audio/wav")
```

然后在软件里：

1. 音色引擎 → **+ 安装模型**
2. 接入方式选 **HTTP 服务**
3. 接口地址填 `http://127.0.0.1:9880/tts`
4. 音色列表填服务支持的音色（voicedesign 可填 `default` 或你在服务里定义的名字）
5. 点「用示例文本测试」，出现“测试成功：产出 xx KB 音频”即接通
6. 保存并安装，之后在播报计划里就能选这个音色

> 参考：MiMo 官方技能文档 <https://github.com/XiaomiMiMo/MiMo-Skills/blob/main/skills/mimo-v2-5-tts/SKILL.md>

## 三、注意事项

- **安全**：命令行模式会在本机执行你填写的命令，只安装可信来源的模型。
- **许可**：第三方模型的版权、公播授权由安装者负责；软件只负责调用。
- **开机自启**：如果接的是本地服务，记得把服务也设为开机启动，否则播报会失败（失败会写日志并回落到其他可用引擎）。
- **路径**：模型或脚本尽量不要放在含中文的路径下；非放不可时，软件已尽量用 ASCII 暂存区规避。
