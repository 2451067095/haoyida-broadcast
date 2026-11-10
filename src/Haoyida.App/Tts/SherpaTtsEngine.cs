using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Haoyida.Tts;

/// <summary>
/// sherpa-onnx（Apache-2.0）+ AISHELL-3 中文多说话人模型的离线引擎。
/// 常驻一个 Python 子进程，避免每句都重新加载 116MB 模型。
/// 注意：模型与 Python 都不认中文路径时，由 AppPaths 负责搬到 ASCII 目录。
/// </summary>
public sealed class SherpaTtsEngine : ITtsEngine
{
    private readonly string _python;
    private readonly string _script;
    private Process? _worker;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _lock = new();

    public string Id => "sherpa";
    public string Name => "sherpa 真人的调";
    public string Vendor => "k2-fsa/sherpa-onnx + AISHELL-3";
    public string License => "Apache-2.0（代码）· 语料许可需核对";
    public string Kind => "离线";
    public string Note => "174 个中文说话人，已精选 6 个不同音色；纯 CPU，首次加载约 2 秒。";

    public SherpaTtsEngine(string pythonExecutable, string scriptPath)
    {
        _python = pythonExecutable;
        _script = scriptPath;
    }

    public string ModelDirectory => AppPaths.SherpaRoot;

    public IReadOnlyList<VoiceOption> Voices { get; } = new List<VoiceOption>
    {
        new("s33", "清亮女声", "sherpa", "女声", "明亮", "AISHELL-3 说话人 33 · 基频约 258Hz", "欢迎光临，今日鲜果特惠，欢迎选购。"),
        new("s0",  "标准女声", "sherpa", "女声", "自然", "AISHELL-3 说话人 0 · 基频约 235Hz", "欢迎光临，今日鲜果特惠，欢迎选购。"),
        new("s7",  "温柔女声", "sherpa", "女声", "柔和", "AISHELL-3 说话人 7 · 基频约 195Hz", "欢迎光临，今日鲜果特惠，欢迎选购。"),
        new("s15", "稳重男声", "sherpa", "男声", "沉稳", "AISHELL-3 说话人 15 · 基频约 148Hz", "顾客朋友们请注意，本店今日有特惠。"),
        new("s60", "浑厚男声", "sherpa", "男声", "厚实", "AISHELL-3 说话人 60 · 基频约 131Hz", "顾客朋友们请注意，本店今日有特惠。"),
        new("s45", "低沉男声", "sherpa", "男声", "低沉", "AISHELL-3 说话人 45 · 基频约 94Hz", "顾客朋友们请注意，本店今日有特惠。")
    };

    public bool IsAvailable(out string reason)
    {
        reason = "";
        if (!File.Exists(_script)) { reason = "缺少 sherpa sidecar 脚本"; return false; }
        if (!Directory.Exists(ModelDirectory)) { reason = "未找到 sherpa 模型目录"; return false; }
        if (!File.Exists(Path.Combine(ModelDirectory, "vits-aishell3.onnx")))
        {
            reason = "模型文件缺失（vits-aishell3.onnx）";
            return false;
        }
        if (Path.IsPathRooted(_python) && !File.Exists(_python)) { reason = "未找到 Python"; return false; }
        return true;
    }

    private void EnsureWorker()
    {
        lock (_lock)
        {
            if (_worker is { HasExited: false }) return;

            var psi = new ProcessStartInfo
            {
                FileName = _python,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                StandardInputEncoding = new UTF8Encoding(false)
            };
            psi.ArgumentList.Add(_script);
            psi.ArgumentList.Add(ModelDirectory);

            var worker = new Process { StartInfo = psi };
            if (!worker.Start()) throw new InvalidOperationException("无法启动 sherpa 子进程");

            var readyLine = worker.StandardOutput.ReadLine();
            if (readyLine is null || !readyLine.Contains("\"ready\": true", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("sherpa 子进程初始化失败：" + (readyLine ?? "无响应"));

            _worker = worker;
        }
    }

    public async Task<string> SynthesizeAsync(string text, VoiceOption voice, double rate, double pitch, string outputPath, CancellationToken ct)
    {
        if (!IsAvailable(out var why)) throw new InvalidOperationException(why);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            EnsureWorker();
            var worker = _worker!;

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            var sid = int.TryParse(voice.Id.TrimStart('s'), out var n) ? n : 0;

            var payload = JsonSerializer.Serialize(new
            {
                text,
                sid,
                speed = Math.Clamp(rate, 0.6, 1.6),
                @out = outputPath
            });

            await worker.StandardInput.WriteLineAsync(payload.AsMemory(), ct).ConfigureAwait(false);
            await worker.StandardInput.FlushAsync(ct).ConfigureAwait(false);

            var readTask = worker.StandardOutput.ReadLineAsync(ct).AsTask();
            var done = await Task.WhenAny(readTask, Task.Delay(TimeSpan.FromSeconds(60), ct)).ConfigureAwait(false);
            if (done != readTask) throw new TimeoutException("sherpa 合成超时");

            var line = readTask.Result;
            if (line is null) throw new InvalidOperationException("sherpa 子进程已退出");

            using var doc = JsonDocument.Parse(line);
            var ok = doc.RootElement.TryGetProperty("ok", out var okEl) && okEl.GetBoolean();
            if (!ok)
            {
                var err = doc.RootElement.TryGetProperty("error", out var e2) ? e2.GetString() : "未知错误";
                throw new InvalidOperationException("sherpa 合成失败：" + err);
            }

            if (!File.Exists(outputPath) || new FileInfo(outputPath).Length < 1000)
                throw new InvalidOperationException("sherpa 输出为空");

            return outputPath;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        try { _worker?.Kill(entireProcessTree: true); } catch { }
        try { _worker?.Dispose(); } catch { }
        _worker = null;
    }
}
