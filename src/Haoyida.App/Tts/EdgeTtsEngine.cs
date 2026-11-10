using System.IO;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Haoyida.Tts;

/// <summary>
/// edge-tts（MIT 许可的客户端，音色来自微软 Edge 朗读服务）在线引擎。
/// 优点：免训练、中文音色多（晓晓/云希/云健/东北话…），适合“不同调调”。
/// 缺点：依赖外网；部分地区 WSS 端点会返回 403，因此必须能优雅降级。
/// </summary>
public sealed class EdgeTtsEngine : ITtsEngine
{
    public string Id => "edge";
    public string Name => "Edge 在线音色";
    public string Vendor => "rany2/edge-tts";
    public string License => "MIT（工具）· 服务条款需自行确认";
    public string Kind => "在线";
    public string Note => "中文音色最多、最像真人；断网或端点被拦时自动回落到离线引擎。";

    private readonly string _python;
    private string? _availabilityError;

    public EdgeTtsEngine(string pythonExecutable) => _python = pythonExecutable;

    public IReadOnlyList<VoiceOption> Voices { get; } = new List<VoiceOption>
    {
        new("zh-CN-XiaoxiaoNeural", "晓晓", "edge", "女声", "温暖", "亲和力强，超市日常播报首选", "欢迎光临，本店今日鲜果特惠，欢迎选购。"),
        new("zh-CN-XiaoyiNeural", "晓伊", "edge", "女声", "活泼", "偏年轻、语气轻快", "限时特惠，买一送一，不要错过。"),
        new("zh-CN-YunxiNeural", "云希", "edge", "男声", "阳光", "年轻男声，语速自然", "本店新到鲜货，欢迎品尝。"),
        new("zh-CN-YunjianNeural", "云健", "edge", "男声", "激昂", "体育解说风格，适合大促", "全场特价，仅限今天，快来抢购！"),
        new("zh-CN-YunyangNeural", "云扬", "edge", "男声", "专业", "新闻播音腔，稳重可信", "温馨提示，请顾客朋友们看管好随身物品。"),
        new("zh-CN-YunxiaNeural", "云夏", "edge", "男声", "少年", "少年音，适合亲子活动", "小朋友，快来参加我们的亲子活动吧。"),
        new("zh-CN-liaoning-XiaobeiNeural", "晓北", "edge", "女声", "东北话", "方言播报，接地气", "老铁们，今天的菜老新鲜了！"),
        new("zh-CN-shaanxi-XiaoniNeural", "晓妮", "edge", "女声", "陕西话", "方言播报，亲切", "乡党们，今日特价，快来瞅一瞅。")
    };

    public bool IsAvailable(out string reason)
    {
        reason = "";
        if (_availabilityError is not null)
        {
            reason = _availabilityError;
            return false;
        }
        if (_available is { } cached)
        {
            if (!cached) reason = "未安装 edge-tts";
            return cached;
        }

        // 快速探测：只看 Python 与 site-packages，避免启动时阻塞几秒。
        try
        {
            if (Path.IsPathRooted(_python))
            {
                if (!File.Exists(_python)) { _available = false; reason = "未找到 Python"; return false; }
                var baseDir = Path.GetDirectoryName(_python)!;
                if (!Directory.Exists(Path.Combine(baseDir, "Lib", "site-packages", "edge_tts")))
                {
                    _available = false;
                    reason = "未安装 edge-tts（pip install edge-tts）";
                    return false;
                }
                _available = true;
                return true;
            }
        }
        catch { }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _python,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add("import edge_tts,sys;sys.stdout.write('ok')");
            using var p = Process.Start(psi);
            if (p is null) { reason = "无法启动 Python"; return false; }
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(8000);
            _available = output.Contains("ok", StringComparison.Ordinal);
            if (_available != true)
            {
                _availabilityError = "未安装 edge-tts（pip install edge-tts）";
                reason = _availabilityError;
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            _available = false;
            _availabilityError = ex.Message;
            reason = ex.Message;
            return false;
        }
    }

    private bool? _available;

    /// <summary>联网自检：真正合成一小段，确认服务端不返回 403。</summary>
    public async Task<(bool Ok, string Message)> ProbeServiceAsync(CancellationToken ct)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "haoyida-edge-probe-" + Guid.NewGuid().ToString("N")[..6] + ".mp3");
        try
        {
            await SynthesizeAsync("测试", Voices[0], 1.0, 1.0, tmp, ct).ConfigureAwait(false);
            return (true, "在线音色可用");
        }
        catch (Exception ex)
        {
            _availabilityError = ex.Message;
            return (false, ex.Message);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }

    public async Task<string> SynthesizeAsync(string text, VoiceOption voice, double rate, double pitch, string outputPath, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var psi = new ProcessStartInfo
        {
            FileName = _python,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        psi.ArgumentList.Add("-m");
        psi.ArgumentList.Add("edge_tts");
        psi.ArgumentList.Add("--voice");
        psi.ArgumentList.Add(voice.Id);
        psi.ArgumentList.Add("--text");
        psi.ArgumentList.Add(text);
        psi.ArgumentList.Add("--write-media");
        psi.ArgumentList.Add(outputPath);
        psi.ArgumentList.Add("--rate");
        psi.ArgumentList.Add($"{(rate >= 1 ? "+" : "")}{(int)Math.Round((rate - 1.0) * 100)}%");
        psi.ArgumentList.Add("--pitch");
        psi.ArgumentList.Add($"{(pitch >= 1 ? "+" : "")}{(int)Math.Round((pitch - 1.0) * 50)}Hz");

        using var process = new Process { StartInfo = psi };
        var stderr = new StringBuilder();
        if (!process.Start()) throw new InvalidOperationException("无法启动 edge-tts");

        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };
        process.BeginErrorReadLine();
        _ = process.StandardOutput.ReadToEndAsync(ct);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));

        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("edge-tts 合成超时");
        }

        if (!File.Exists(outputPath) || new FileInfo(outputPath).Length < 1000)
        {
            var detail = stderr.ToString().Trim();
            if (detail.Contains("403"))
            {
                _available = false;
                _availabilityError = "在线音色被服务端拦截(403)，当前网络不可用";
                throw new InvalidOperationException(_availabilityError);
            }
            throw new InvalidOperationException("在线合成失败：" + (detail.Length > 200 ? detail[^200..] : detail));
        }

        return outputPath;
    }
}
