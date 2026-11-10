using System.IO;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Haoyida.Tts;

/// <summary>
/// Piper（MIT 许可）本地神经网络 TTS：调用 piper.exe + *.onnx 音色模型。
/// 模型与可执行文件放在程序目录的 piper\ 下，完全离线。
/// </summary>
public sealed class PiperTtsEngine : ITtsEngine
{
    public string Id => "piper";
    public string Name => "Piper 神经语音";
    public string Vendor => "rhasspy/piper";
    public string License => "MIT（代码）";
    public string Kind => "离线";
    public string Note => "本地 ONNX 推理，不联网、不要 GPU；音色质量明显高于系统合成音。";

    private readonly string _root;
    private IReadOnlyList<VoiceOption>? _voices;

    public PiperTtsEngine(string root) => _root = root;

    public string ExePath => Path.Combine(_root, "piper.exe");
    public string VoicesDirectory => Path.Combine(_root, "voices");

    public bool IsAvailable(out string reason)
    {
        reason = "";
        if (!File.Exists(ExePath))
        {
            reason = $"未找到 piper.exe（预期位置：{ExePath}）";
            return false;
        }
        if (Voices.Count == 0)
        {
            reason = "未找到任何 .onnx 音色模型";
            return false;
        }
        return true;
    }

    public IReadOnlyList<VoiceOption> Voices => _voices ??= Probe();

    private IReadOnlyList<VoiceOption> Probe()
    {
        var list = new List<VoiceOption>();
        if (!Directory.Exists(VoicesDirectory)) return list;

        foreach (var onnx in Directory.EnumerateFiles(VoicesDirectory, "*.onnx"))
        {
            var baseName = Path.GetFileNameWithoutExtension(onnx);
            string lang = "未知", dataset = "", speakers = "1";
            var cfg = onnx + ".json";
            if (File.Exists(cfg))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(cfg));
                    var root = doc.RootElement;
                    if (root.TryGetProperty("language", out var l)) lang = l.GetProperty("name_native").GetString() ?? lang;
                    if (root.TryGetProperty("dataset", out var ds)) dataset = ds.GetString() ?? "";
                    if (root.TryGetProperty("num_speakers", out var ns)) speakers = ns.ToString();
                }
                catch { /* 配置损坏不影响使用 */ }
            }

            bool zh = baseName.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
            list.Add(new VoiceOption(
                Id: "piper:" + baseName,
                Name: PrettyName(baseName),
                EngineId: "piper",
                Gender: baseName.Contains("huayan", StringComparison.OrdinalIgnoreCase) ? "女声" : "—",
                Style: zh ? "自然" : "外语",
                Description: $"{lang} · {dataset} · 说话人 {speakers}",
                SampleText: "亲爱的顾客朋友们，欢迎光临，本店今日生鲜特惠，欢迎选购。"));
        }

        return list.OrderBy(v => v.Id).ToList();
    }

    private static string PrettyName(string baseName)
    {
        var parts = baseName.Split('-');
        if (parts.Length >= 2) return $"{parts[1]}（{parts[0]}）";
        return baseName;
    }

    public async Task<string> SynthesizeAsync(string text, VoiceOption voice, double rate, double pitch, string outputPath, CancellationToken ct)
    {
        var modelName = voice.Id.StartsWith("piper:", StringComparison.Ordinal) ? voice.Id[6..] : voice.Id;
        var model = Path.Combine(VoicesDirectory, modelName + ".onnx");
        if (!File.Exists(model)) throw new FileNotFoundException("找不到音色模型：" + model);

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        // piper.exe 不认中文路径：输出先写到纯 ASCII 暂存区，再搬回缓存目录。
        var scratch = Path.Combine(AppPaths.AsciiScratchDirectory,
            "piper-" + Guid.NewGuid().ToString("N")[..8] + ".wav");

        // 音调因子：>1 更清亮，<1 更低沉。先按 r 倍拉长，合成后再按 r 倍重采样，
        // 净效果是“时长不变、音调变化”，用同一个音色派生出不同“调调”。
        double pitchFactor = Math.Clamp(pitch, 0.86, 1.16);

        // Piper 用 length_scale 表示“每个音素的时长”，与语速成反比。
        double lengthScale = Math.Clamp(1.0 / Math.Max(0.5, rate) * pitchFactor, 0.5, 2.5);
        double noiseW = 0.8;

        var psi = new ProcessStartInfo
        {
            FileName = ExePath,
            WorkingDirectory = _root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        psi.ArgumentList.Add("--model");
        psi.ArgumentList.Add(model);
        psi.ArgumentList.Add("--output_file");
        psi.ArgumentList.Add(scratch);
        psi.ArgumentList.Add("--length_scale");
        psi.ArgumentList.Add(lengthScale.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
        psi.ArgumentList.Add("--noise_scale");
        psi.ArgumentList.Add("0.667");
        psi.ArgumentList.Add("--noise_w");
        psi.ArgumentList.Add(noiseW.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
        psi.ArgumentList.Add("--sentence_silence");
        psi.ArgumentList.Add("0.2");

        using var process = new Process { StartInfo = psi };
        var stderr = new StringBuilder();

        if (!process.Start()) throw new InvalidOperationException("无法启动 piper.exe");

        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };
        process.BeginErrorReadLine();
        _ = process.StandardOutput.ReadToEndAsync(ct);

        await process.StandardInput.WriteAsync(text.AsMemory(), ct).ConfigureAwait(false);
        await process.StandardInput.WriteLineAsync().ConfigureAwait(false);
        process.StandardInput.Close();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));

        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("Piper 合成超时");
        }

        try
        {
            if (process.ExitCode != 0 || !File.Exists(scratch) || new FileInfo(scratch).Length < 1000)
                throw new InvalidOperationException($"Piper 合成失败（退出码 {process.ExitCode}）：{stderr.ToString().Trim()}");

            if (Math.Abs(pitchFactor - 1.0) > 0.02)
            {
                WavUtil.Resample(scratch, outputPath, pitchFactor);
            }
            else
            {
                File.Move(scratch, outputPath, overwrite: true);
            }
            return outputPath;
        }
        finally
        {
            try { if (File.Exists(scratch)) File.Delete(scratch); } catch { }
        }
    }
}
