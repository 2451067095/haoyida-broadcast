using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Haoyida.Tts;

/// <summary>
/// Kokoro-82M v1.1-zh（Apache-2.0）中文多音色引擎。
/// 常驻 Python 子进程：模型加载一次，之后逐句合成（CPU 约 2–3 秒/句，结果有缓存）。
/// </summary>
public sealed class KokoroTtsEngine : ITtsEngine
{
    private readonly string _python;
    private readonly string _script;
    private Process? _worker;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _lock = new();

    public string Id => "kokoro";
    public string Name => "Kokoro 中文多音色";
    public string Vendor => "hexgrad/Kokoro-82M-v1.1-zh";
    public string License => "Apache-2.0";
    public string Kind => "离线";
    public string Note => "100 个中文音色（8 女 + 8 男精选），纯 CPU，首次加载约 3 秒。";

    public KokoroTtsEngine(string python, string script)
    {
        _python = python;
        _script = script;
    }

    public IReadOnlyList<VoiceOption> Voices { get; } = new List<VoiceOption>
    {
        new("zf_001", "女声 01", "kokoro", "女声", "自然", "Kokoro zh zf_001", "欢迎光临，本店今日鲜果特惠。"),
        new("zf_003", "女声 02", "kokoro", "女声", "明亮", "Kokoro zh zf_003", "欢迎光临，本店今日鲜果特惠。"),
        new("zf_005", "女声 03", "kokoro", "女声", "温柔", "Kokoro zh zf_005", "欢迎光临，本店今日鲜果特惠。"),
        new("zf_021", "女声 04", "kokoro", "女声", "亲切", "Kokoro zh zf_021", "欢迎光临，本店今日鲜果特惠。"),
        new("zf_060", "女声 05", "kokoro", "女声", "活泼", "Kokoro zh zf_060", "欢迎光临，本店今日鲜果特惠。"),
        new("zf_088", "女声 06", "kokoro", "女声", "成熟", "Kokoro zh zf_088", "欢迎光临，本店今日鲜果特惠。"),
        new("zm_009", "男声 01", "kokoro", "男声", "沉稳", "Kokoro zh zm_009", "顾客朋友们请注意，本店今日有特惠。"),
        new("zm_010", "男声 02", "kokoro", "男声", "浑厚", "Kokoro zh zm_010", "顾客朋友们请注意，本店今日有特惠。"),
        new("zm_013", "男声 03", "kokoro", "男声", "阳光", "Kokoro zh zm_013", "顾客朋友们请注意，本店今日有特惠。"),
        new("zm_020", "男声 04", "kokoro", "男声", "播音腔", "Kokoro zh zm_020", "顾客朋友们请注意，本店今日有特惠。"),
        new("zm_045", "男声 05", "kokoro", "男声", "低沉", "Kokoro zh zm_045", "顾客朋友们请注意，本店今日有特惠。"),
        new("zm_100", "男声 06", "kokoro", "男声", "稳重", "Kokoro zh zm_100", "顾客朋友们请注意，本店今日有特惠。")
    };

    public bool IsAvailable(out string reason)
    {
        reason = "";
        if (!File.Exists(_script)) { reason = "缺少 kokoro sidecar 脚本"; return false; }
        var dir = AppPaths.KokoroRoot;
        if (!Directory.Exists(dir)) { reason = "未找到 Kokoro 模型目录"; return false; }
        if (!File.Exists(Path.Combine(dir, "tokenizer.json"))) { reason = "模型文件不完整"; return false; }
        var hasOnnx = Directory.Exists(Path.Combine(dir, "onnx")) &&
                      Directory.GetFiles(Path.Combine(dir, "onnx"), "*.onnx").Length > 0;
        if (!hasOnnx) { reason = "缺少 onnx 模型"; return false; }
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
            psi.ArgumentList.Add(AppPaths.KokoroRoot);

            var worker = new Process { StartInfo = psi };
            if (!worker.Start()) throw new InvalidOperationException("无法启动 Kokoro 子进程");

            var deadline = DateTime.Now.AddSeconds(40);
            bool ready = false;
            while (DateTime.Now < deadline)
            {
                var line = worker.StandardOutput.ReadLine();
                if (line is null) break;
                if (line.Contains("\"ready\": true", StringComparison.OrdinalIgnoreCase)) { ready = true; break; }
            }
            if (!ready)
            {
                try { worker.Kill(true); } catch { }
                var err = "";
                try { err = worker.StandardError.ReadToEnd(); } catch { }
                throw new InvalidOperationException("Kokoro 初始化失败：" + (err.Length > 300 ? err[^300..] : err));
            }

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
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

            var payload = JsonSerializer.Serialize(new
            {
                text,
                voice = voice.Id,
                speed = Math.Clamp(rate, 0.6, 1.6),
                @out = outputPath
            });

            await _worker!.StandardInput.WriteLineAsync(payload.AsMemory(), ct).ConfigureAwait(false);
            await _worker.StandardInput.FlushAsync(ct).ConfigureAwait(false);

            var read = _worker.StandardOutput.ReadLineAsync(ct).AsTask();
            var done = await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(120), ct)).ConfigureAwait(false);
            if (done != read) throw new TimeoutException("Kokoro 合成超时");

            var line = read.Result ?? throw new InvalidOperationException("Kokoro 子进程已退出");
            using var doc = JsonDocument.Parse(line);
            var ok = doc.RootElement.TryGetProperty("ok", out var okEl) && okEl.GetBoolean();
            if (!ok)
            {
                var err = doc.RootElement.TryGetProperty("error", out var e2) ? e2.GetString() : "未知错误";
                throw new InvalidOperationException("Kokoro 合成失败：" + err);
            }

            if (!File.Exists(outputPath) || new FileInfo(outputPath).Length < 1000)
                throw new InvalidOperationException("Kokoro 输出为空");

            return outputPath;
        }
        finally
        {
            _gate.Release();
        }
    }
}
