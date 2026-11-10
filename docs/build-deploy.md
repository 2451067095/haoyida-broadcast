# 构建与门店部署

## 1. 环境要求

| 用途 | 需要 |
| --- | --- |
| 构建 | .NET 10 SDK（本项目在 `10.0.401` 上验证） |
| 运行（目标机） | .NET 10 Desktop Runtime（WPF）；Windows 10 1809+ / Windows 11 |
| 可选 | Python 3.10+ 与 `pip install edge-tts`（仅在线音色需要） |

本项目**不引用任何 NuGet 包**，因此可以在没有外网、甚至 TLS 栈异常的机器上离线构建。

## 2. 构建

```powershell
cd src/Haoyida.App
dotnet build -c Release
```

如果构建机的用户目录被安全软件/沙箱限制，给 dotnet 指定可写目录：

```powershell
$env:DOTNET_CLI_HOME = "D:\build\dotnet-home"
$env:NUGET_PACKAGES = "D:\build\nuget"
dotnet build -c Release
```

## 3. 发布

```powershell
dotnet publish -c Release -r win-x64 --self-contained false -o ..\..\dist\Haoyida
```

- `--self-contained false`：产物约 2 MB，但目标机必须装 Desktop Runtime。
- 若要做自包含单文件（约 150 MB），需要能访问 NuGet 拉取 runtime pack；内网受限时不可行。

## 4. 内置语音运行时

发布后把 Piper 运行时放进 `dist/Haoyida/piper/`：

```
dist/Haoyida/
├─ Haoyida.exe
├─ Haoyida.dll
└─ piper/
   ├─ piper.exe
   ├─ onnxruntime.dll / espeak-ng.dll / piper_phonemize.dll / *.dll
   ├─ espeak-ng-data/
   ├─ libtashkeel_model.ort
   └─ voices/
      ├─ zh_CN-huayan-medium.onnx
      └─ zh_CN-huayan-medium.onnx.json
```

获取方式：

```powershell
# 二进制（MIT）
curl -L -o piper.zip https://github.com/rhasspy/piper/releases/download/2023.11.14-2/piper_windows_amd64.zip
# 中文音色
curl -L -o zh_CN-huayan-medium.onnx https://huggingface.co/rhasspy/piper-voices/resolve/main/zh/zh_CN/huayan/medium/zh_CN-huayan-medium.onnx
curl -L -o zh_CN-huayan-medium.onnx.json https://huggingface.co/rhasspy/piper-voices/resolve/main/zh/zh_CN/huayan/medium/zh_CN-huayan-medium.onnx.json
```

## 5. 首次运行会发生什么

1. 创建数据目录 `%LOCALAPPDATA%\Haoyida`（配置 + 语音缓存）。
2. 若程序所在路径含中文，自动把 `piper/` 复制到 `C:\ProgramData\Haoyida\runtime\piper` 并写 `.stamp`（约 60 MB，只做一次）。
3. 读取 `store.json`；不存在则写入示例门店、6 条文案、6 个商品、4 条计划。
4. 启动调度器与 BGM（若已选择）。

## 6. 部署到门店

1. 拷贝整个 `dist\Haoyida` 到门店电脑，建议放 `D:\Haoyida`（**路径含中文也能跑**，程序会自行搬运 Piper）。
2. 双击 `Haoyida.exe`，进入「系统设置」填写门店名、营业时间、电话。
3. 在「音色引擎」页选音色并试听；在「文案库」「促销商品」维护内容。
4. 在「播报计划」里建计划（例如 08:00–21:30 每 45 分钟轮播生鲜）。
5. 选择 BGM（自备无版权音乐），设置 BGM 音量（建议 30%）与总音量。
6. 开机自启：`Win+R` → `shell:startup` → 放入 `Haoyida.exe` 的快捷方式。

### 音频输出

程序使用系统默认播放设备。门店若接功放，请在 Windows 声音设置里把功放设为默认设备，并关闭“音频增强/空间音效”。

## 6.1 合规提示

「背景音乐 → 添加本地音频」旁固定显示“个人会员 ≠ 公播授权”提醒，点「查看说明」打开完整合规弹窗（含音著协/音集协/云村/曲易买入口与费用参考）。
内置三首 BGM 为本项目原创合成，可放心使用；用户自行添加的音频需自行取得公播授权。

## 7. 验收

```powershell
# 端到端自检：引擎枚举 + 真实合成 + 金额读法
$env:HAOYIDA_LOG="D:\haoyida-selftest.log"
Haoyida.exe --selftest
Get-Content D:\haoyida-selftest.log

# 页面走查截图（8 张 PNG）
Haoyida.exe --shots D:\shots

# 单独渲染“背景音乐版权合规”弹窗
Haoyida.exe --shot-dialog D:\shots\09-compliance.png
```

期望日志：

```
engine piper available=True voices=1
engine sapi  available=True voices=2
price 6.98=六块九毛八 | 6.90=六块九 | 12.00=十二块整
selftest piper -> ...\cache\xxxx.wav bytes=248992 cache=False actual=piper
```

## 8. 排错

| 现象 | 原因 | 处理 |
| --- | --- | --- |
| 启动即退出 / 弹“启动失败” | XAML 资源或运行库缺失 | 看 `%LOCALAPPDATA%\Haoyida\startup.log` |
| 音色页 Piper “未找到 piper.exe” | `piper/` 目录没跟着发布 | 按第 4 节补齐 |
| Piper 合成失败，退出码 `-1073740791` | 路径含中文（`0xC0000409`） | 让程序自行搬运，或把整个软件放到纯英文路径 |
| 在线音色播报失败 / 提示 403 | WSS 端点被地区限制 | 改用 Piper/SAPI；或换 Azure 语音服务 |
| 没有中文音色（SAPI） | 系统未安装中文语音包 | 设置 → 时间和语言 → 语音 → 添加中文语音 |
| 播报没有声音但日志“成功” | 默认输出设备不对 / 静音 | 检查系统默认播放设备与音量合成器 |
| 日志里 `elapsed 0 ms` | 命中缓存 | 正常现象（同一句话第二次播报） |

## 9. 数据备份与迁移

- 备份：直接复制 `%LOCALAPPDATA%\Haoyida`（`store.json` + `bak`）。
- 迁移：拷到新机器同一路径即可；语音缓存可删（会自动重建）。
- 崩溃恢复：程序写 `store.json.tmp` 后原子替换，并保留 `store.json.bak`；若主文件损坏会自动改名 `*.broken-时间戳` 并用示例数据启动。
