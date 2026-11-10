# 开源 TTS 选型与许可

目标：为超市播音找到**可商用、中文自然、能离线**的语音方案，并且允许以后换引擎而不改业务代码。

> 许可证信息采集自各仓库 GitHub API / LICENSE 原文（2026-11 核对）。**商用前请以仓库最新 LICENSE 与模型卡为准**；标“需核对”的表示未逐字验证。

## 一、本项目已落地

### 1. Piper（默认，离线）

- 仓库：<https://github.com/rhasspy/piper> · 许可证：**MIT**（GitHub API 确认）
- 音色：`zh_CN-huayan-medium`（63 MB ONNX，女声）
- 实测：本机 CPU 推理 RTF ≈ **0.14**，3.5 秒音频约 0.5 秒合成；首句含模型加载约 4.8 秒
- 优点：单文件 exe + onnx，无需 Python/PyTorch；断网可用；延迟低
- 缺点：**中文只有 1 个音色**；“不同调调”只能靠语速/音调/风格预设
- 坑：`piper.exe` 对**非 ASCII 路径**会崩溃（`0xC0000409`）。本项目自动搬运运行时到 `C:\ProgramData\Haoyida\runtime\piper`，并把输出先写 ASCII 暂存区

### 2. Windows SAPI5（兜底，离线）

- 来源：系统组件，随 Windows 授权
- 实测：本机可用 `Microsoft Huihui Desktop`（中文女声）与 `Zira`（英文）
- 优点：零依赖、无需下载、必定可用
- 缺点：合成音机械感明显，只适合兜底

### 3. edge-tts（在线，可选）

- 仓库：<https://github.com/rany2/edge-tts> · 许可证：**MIT**（工具本身）
- 音色：8 个中文（晓晓/晓伊/云希/云健/云扬/云夏 + 东北话晓北 + 陕西话晓妮），最像真人
- 实测：REST 列表可用；**WSS 合成端点在本机返回 403**（地区/风控），因此程序把它标记为“需联网”，失败自动回落 SAPI
- 风险：调用的是微软 Edge 朗读的未公开接口，**不属于官方商用 API**；正式商用建议换成 Azure 语音服务等有 SLA 的授权渠道

## 二、值得接入的候选（按落地成本排序）

| 模型 | 中文 | 离线 | 许可 | 落地方式 | 评价 |
| --- | --- | --- | --- | --- | --- |
| [Kokoro-82M](https://github.com/hexgrad/kokoro) | ✅ 8 音色 | ✅ | **Apache-2.0** | onnxruntime（C# 可行）或 Python sidecar，模型 310 MB | 82M 参数，CPU 友好，是“多音色离线”的最佳补位；中文音色含 zf_xiaobei/zm_yunjian 等 |
| [MeloTTS](https://github.com/myshell-ai/MeloTTS) | ✅ 中英混 | ✅ | MIT（需核对仓库 LICENSE） | PyTorch，依赖较重 | 中英混读自然，适合进口商品名 |
| [CosyVoice](https://github.com/FunAudioLLM/CosyVoice) | ✅ | ✅ | **Apache-2.0**（LICENSE 原文确认） | PyTorch + 参考音频，需 GPU 更佳 | 零样本克隆，可做“店长音色”；体量大 |
| [GPT-SoVITS](https://github.com/RVC-Boss/GPT-SoVITS) | ✅ | ✅ | MIT（需核对） | PyTorch，少量录音即可克隆 | 定制门店专属音色最省数据；**克隆真人需取得授权** |
| [IndexTTS](https://github.com/index-tts/index-tts) | ✅ | ✅ | 仓库标注 **NOASSERTION / Other**，代码与权重条款需分别核对 | PyTorch | 工业级可控性，效果强；许可需法务确认后再商用 |
| [F5-TTS](https://github.com/SWivid/F5-TTS) | ✅ | ✅ | 代码 MIT；**权重受 Emilia 数据集 CC-BY-NC 约束** | PyTorch | 效果接近商用，但**默认权重不可商用**，需自训 |
| [Fish-Speech](https://github.com/fishaudio/fish-speech) | ✅ | ✅ | 各版本不同（部分权重 CC-BY-NC-SA） | PyTorch | 音色丰富；务必逐版本核对权重许可 |
| [ChatTTS](https://github.com/2noise/ChatTTS) | ✅ | ✅ | **AGPL-3.0**（GitHub API 确认），权重另有限制 | PyTorch | 对话语气极自然，但 **AGPL + 非商用限制**，闭源商用不建议 |

## 三、选型结论

1. **现在能交付的**：Piper（默认）+ SAPI（兜底）。零下载、离线、许可干净。
2. **想立刻多几种音色**：接 Kokoro（Apache-2.0，8 个中文音色）。C# 侧用 `Microsoft.ML.OnnxRuntime` 即可，不需要 Python。
3. **想要“店长本人”的音色**：GPT-SoVITS 少量录音克隆；必须拿到本人书面授权，避免人格权/声音权纠纷。
4. **不要碰**：ChatTTS（AGPL + 非商用）、F5-TTS 默认权重（NC）、任何来路不明的“商用授权”音色包。

## 四、如何接入一个新引擎

业务层只依赖 `ITtsEngine`（`src/Haoyida.App/Tts/ITtsEngine.cs`）：

```csharp
public interface ITtsEngine
{
    string Id { get; }            // "kokoro"
    string Name { get; }          // "Kokoro 神经语音"
    string Vendor { get; }        // 上游仓库
    string License { get; }       // 展示给门店的许可说明
    string Kind { get; }          // 离线 / 在线
    string Note { get; }
    bool IsAvailable(out string reason);
    IReadOnlyList<VoiceOption> Voices { get; }
    Task<string> SynthesizeAsync(string text, VoiceOption voice, double rate, double pitch, string outputPath, CancellationToken ct);
}
```

新增一个 `KokoroTtsEngine`，在 `TtsService` 构造函数里注册即可；缓存、回落、队列、日志、试听全部自动复用。

接入检查清单：

- [ ] `IsAvailable` 要“快”（≤ 200 ms），启动时会被调用
- [ ] 输出文件 > 1 KB 才认为成功（避免空音频进缓存）
- [ ] 路径必须是 ASCII（若底层是 C++ 程序）
- [ ] 失败抛异常即可，`SynthAsync` 会自动回落 SAPI
- [ ] 在 `docs` 里补充该引擎的许可证与商用边界

## 五、本项目实测记录（2026-11）

| 尝试 | 结果 | 结论 |
| --- | --- | --- |
| Piper `zh_CN-huayan-medium` | ✅ 默认引擎，RTF≈0.14 | 采用 |
| Windows SAPI（Huihui） | ✅ 兜底可用；COM 调用需在 **STA 线程**，偶发 `ArgumentException` 已加重试 | 采用 |
| edge-tts 7.2.8 | ⚠️ 列表接口可用，**WSS 合成 403**（地区限制） | 保留接入位，失败自动回落 |
| Kokoro-82M (`kokoro-v1.0.onnx` + `voices-v1.0.bin`) | ❌ 用 `misaki.zh` 音素喂入只产出 0.85 秒乱音 | **v1.0 权重不含中文**，需 v1.1-zh |
| sherpa-onnx 2.0.1 + `vits-zh-aishell3` | 🟡 管道打通：174 说话人、加载 2.0s、合成 1.5–2.9s/句；但本配置输出 **8kHz**，音质不达标 | 代码保留（`Tts/SherpaTtsEngine.cs` + `tools/sherpa_tts_worker.py`），默认不启用 |

### Piper 的“一个音色多种调调”怎么实现

Piper 只有 1 个中文女声。为了有听感差异，本项目在合成链路上做了**真实变调**：

1. 生成时 `length_scale *= r`（先把音频拉长 r 倍）；
2. 合成后按 `r` 倍重采样（`Tts/WavUtil.cs` 线性插值）；
3. 净效果：**音调变化、语速大致不变**。

`r=0.88` 更低沉、`r=1.12` 更清亮；配合 6 种风格预设（亲切/稳重/活力/温柔/播报腔/清晰）与语速微调，同一模型可覆盖大部分门店场景。

### 启用 sherpa 多音色的下一步

1. 换 22.05kHz 的模型（如 `vits-melo-tts-zh_en`），或修正 `OfflineTts` 的采样率配置；
2. 把 `TtsService` 里的 `Engines = { piper, _sapi, edge }` 改回包含 `sherpa`；
3. 用 `Haoyida.exe --selftest` 验证 `actual=sherpa` 且 WAV 时长为 4–6 秒。
