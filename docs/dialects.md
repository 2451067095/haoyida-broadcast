# 方言音色方案（A：粤语开源模型 / B：GPT-SoVITS 克隆）

## 结论先说

- **粤语**：有免费开源模型（sherpa-onnx VITS 粤语），CPU 可跑，能直接接进软件。
- **其他方言**（川渝、东北、河南、陕西、闽南、客家…）：**没有可用的开源 TTS 权重**，只能走 GPT-SoVITS 声音克隆。
- 克隆真人声音必须取得**书面授权**（声音权/人格权），这是硬前提。

---

## A. 粤语：接 sherpa-onnx 粤语模型

### 1. 模型

在 [sherpa-onnx tts-models](https://github.com/k2-fsa/sherpa-onnx/releases/tag/tts-models) 里找粤语（Cantonese / yue）VITS 模型，常见命名含 `cantonese`、`yue`、`zh-hk`。
下载后解压，目录里应有：`model.onnx`、`tokens.txt`、`lexicon.txt`（部分模型带 `espeak-ng-data`）。

> 注意：不同模型采样率不同（16k / 22.05k / 8k）。**8k 的不要用**，音质不达标——之前 AISHELL-3 那个就是 8k，所以我没启用。

### 2. 接入方式（二选一）

**方式一：内置引擎（推荐，需改代码）**
把模型目录放到 `dist/Haoyida/sherpa-yue/`，在 `TtsService` 里注册
`new SherpaTtsEngine(python, Assets/tts/sherpa_tts_worker.py, modelDir="sherpa-yue")`，
并把音色标成「粤语」。

**方式二：不改代码，用本地服务**
用 `tools/local_tts_service.py` 的同一套 HTTP 协议包一层 sherpa 粤语推理，
然后在软件里「音色引擎 → 第三方语音模型 → + 安装模型 → HTTP」填地址即可。

---

## B. 其他方言：GPT-SoVITS 克隆

### 1. 录音要求

| 项 | 要求 |
| --- | --- |
| 时长 | 3–5 分钟有效语音（脚本见下） |
| 环境 | 安静房间，无回声、无背景音乐，手机距离嘴 15–20cm |
| 格式 | wav / mp3 均可，单声道，采样率 ≥ 16k |
| 语气 | 自然说话，不要播音腔；语速接近日常播报 |
| 内容 | 越多越好，但**必须是你自己的声音或已获授权的人** |

### 2. 录音脚本（20 句，超市场景，可换成方言念）

1. 欢迎光临，本店今天生鲜特价。
2. 新鲜的水果刚到货，欢迎尝一尝。
3. 这个苹果又脆又甜，今天特价。
4. 本地小油菜，早上刚摘的。
5. 五花肉今天搞活动，便宜卖。
6. 鸡蛋买两斤送半斤。
7. 大米今天打折，整袋更划算。
8. 牛奶买一箱送两盒。
9. 散装商品请先称重再结账。
10. 称重台在生鲜区入口。
11. 请顾客朋友们看管好随身物品。
12. 带小孩的顾客请看护好孩子。
13. 购物车请不要推上扶梯。
14. 本店会员免费办理，积分可抵现。
15. 会员日双倍积分，详情问服务台。
16. 收银台在出口右手边。
17. 本店营业到晚上十点。
18. 生鲜商品售出不退换，请当面挑选。
19. 感谢光临，欢迎下次再来。
20. 祝您购物愉快，一路平安。

### 3. 克隆与接入

```powershell
# 1) 准备 GPT-SoVITS（官方仓库，MIT）
git clone https://github.com/RVC-Boss/GPT-SoVITS
cd GPT-SoVITS
pip install -r requirements.txt

# 2) 用官方 WebUI 完成：音频切片 → ASR 标注 → 训练 → 推理
python webui.py

# 3) 训练好后起一个 HTTP 服务（社区有 api_v2.py），例如
python api_v2.py -a 127.0.0.1 -p 9880 -c GPT_SoVITS/configs/tts_infer.yaml
```

然后在软件里：

1. 音色引擎 → **+ 安装模型**
2. 接入方式：**HTTP 服务**
3. 接口地址：`http://127.0.0.1:9880/tts`
4. 请求体模板（按 api_v2 的字段改）：
   ```json
   {"text":"{text}","text_lang":"zh","ref_audio_path":"D:/voices/dialect_ref.wav","prompt_text":"参考音频对应文字","prompt_lang":"zh","media_type":"wav"}
   ```
5. 音色列表填：`方言店长音`
6. 点「用示例文本测试」→ 成功后保存

> 若服务返回的是 base64 JSON，软件也能识别 `{"audio":"..."}` 这种格式。

---

## 授权与合规

- 克隆他人声音：必须签署《声音使用授权书》，写明用途（门店广播）、期限、可撤销。
- 员工离职后应停用并删除其音色模型。
- 方言音色同样属于“人声”，不能拿网上的方言音频直接克隆。
- 商用前建议在「关于」页注明所用第三方模型与许可。

---

## 下一步（软件侧要做的事）

1. 「方言音色包」向导：选方言 → 显示脚本 → 录音保存 → 自动生成 GPT-SoVITS 启动命令与模型条目；
2. 粤语模型内置（选 16k/22.05k 版本，避开 8k）；
3. 方言音色在「播报计划」里可按任务单独指定。
