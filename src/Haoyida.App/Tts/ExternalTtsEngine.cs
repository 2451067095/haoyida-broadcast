using System.Text.Json;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;

namespace Haoyida.Tts;

/// <summary>
/// 第三方语音模型适配器：让用户把任意本地模型（命令行）或推理服务（HTTP）接进来。
/// 用法：在「音色引擎 → 第三方语音模型」里填命令/接口地址，占位符 {text} {out} {voice} {speed}。
/// </summary>
public sealed class ExternalTtsEngine : ITtsEngine
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(120) };
    private readonly Core.CustomTtsModel _model;

    public ExternalTtsEngine(Core.CustomTtsModel model) => _model = model;

    public string Id => "ext:" + _model.Id;
    public string Name => string.IsNullOrWhiteSpace(_model.Name) ? "第三方模型" : _model.Name;
    public string Vendor => "用户安装";
    public string License => string.IsNullOrWhiteSpace(_model.License) ? "许可未标注（请自行确认）" : _model.License;
    public string Kind => _model.Mode == "http" ? "在线/局域网" : "离线";
    public string Note => string.IsNullOrWhiteSpace(_model.Notes)
        ? "由用户自行安装的第三方语音模型，授权与合规由安装者负责。"
        : _model.Notes;

    public IReadOnlyList<VoiceOption> Voices =>
        _model.VoiceNames.Select(v => new VoiceOption(
            Id: v,
            Name: v,
            EngineId: Id,
            Gender: "—",
            Style: "自定义",
            Description: _model.Name,
            SampleText: "欢迎光临，本店今日有特惠。")).ToList();

    public bool IsAvailable(out string reason)
    {
        reason = "";
        if (_model.Mode == "http")
        {
            if (!Uri.TryCreate(_model.Command, UriKind.Absolute, out _)) { reason = "接口地址无效"; return false; }
            if (!string.IsNullOrWhiteSpace(_model.HealthUrl) && !Core.LocalServiceManager.IsReady(_model.Id))
            {
                reason = "本地服务未启动";
                return false;
            }
            return true;
        }

        if (string.IsNullOrWhiteSpace(_model.Command)) { reason = "未填写命令"; return false; }
        return true;
    }

    public async Task<string> SynthesizeAsync(string text, VoiceOption voice, double rate, double pitch, string outputPath, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        // 第三方程序多半也是 C++/老运行时：统一走 ASCII 暂存区，成功后再搬回去。
        var scratch = Path.Combine(AppPaths.AsciiScratchDirectory,
            "ext-" + Guid.NewGuid().ToString("N")[..8] + ".wav");

        if (_model.Mode == "http")
            await SynthesizeHttpAsync(text, voice.Id, rate, scratch, ct).ConfigureAwait(false);
        else
            await SynthesizeCliAsync(text, voice.Id, rate, scratch, ct).ConfigureAwait(false);

        if (!File.Exists(scratch) || new FileInfo(scratch).Length < 512)
            throw new InvalidOperationException("第三方模型没有产出有效音频");

        File.Copy(scratch, outputPath, overwrite: true);
        try { File.Delete(scratch); } catch { }
        return outputPath;
    }

    private async Task SynthesizeCliAsync(string text, string voice, double rate, string scratch, CancellationToken ct)
    {
        var command = _model.Command
            .Replace("{text}", text)
            .Replace("{out}", scratch)
            .Replace("{voice}", voice)
            .Replace("{speed}", rate.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c " + command,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = string.IsNullOrWhiteSpace(_model.WorkDir) || !Directory.Exists(_model.WorkDir)
                ? AppPaths.AsciiScratchDirectory
                : _model.WorkDir
        };

        using var process = new Process { StartInfo = psi };
        var stderr = new StringBuilder();
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        if (!process.Start()) throw new InvalidOperationException("无法启动第三方模型命令");
        process.BeginErrorReadLine();
        _ = process.StandardOutput.ReadToEndAsync(ct);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(180));
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("第三方模型执行超时");
        }

        if (process.ExitCode != 0 && !File.Exists(scratch))
            throw new InvalidOperationException($"第三方模型退出码 {process.ExitCode}：{Truncate(stderr.ToString(), 240)}");
    }

    private async Task SynthesizeHttpAsync(string text, string voice, double rate, string scratch, CancellationToken ct)
    {
        var body = _model.BodyTemplate
            .Replace("{text}", JsonEncode(text))
            .Replace("{voice}", JsonEncode(voice))
            .Replace("{speed}", rate.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));

        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await Http.PostAsync(_model.Command, content, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"接口返回 {(int)response.StatusCode}");

        var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        if (bytes.Length < 512) throw new InvalidOperationException("接口没有返回音频数据");

        // 内容是 JSON（如 {audio: base64}）时，尽量取出 base64 音频。
        if (bytes.Length > 0 && bytes[0] == (byte)'{')
        {
            var json = Encoding.UTF8.GetString(bytes);
            using var doc = JsonDocument.Parse(json);
            foreach (var key in new[] { "audio", "data", "audio_base64", "wav" })
            {
                if (doc.RootElement.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.String)
                {
                    bytes = Convert.FromBase64String(el.GetString()!);
                    break;
                }
            }
        }

        await File.WriteAllBytesAsync(scratch, bytes, ct).ConfigureAwait(false);
    }

    private static string JsonEncode(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");

    private static string Truncate(string s, int n) => string.IsNullOrEmpty(s) ? "无输出" : (s.Length <= n ? s : s[^n..]);
}
