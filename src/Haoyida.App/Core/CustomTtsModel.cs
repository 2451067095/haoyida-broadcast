using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Haoyida.Core;

/// <summary>用户自行安装的第三方语音模型（命令行或 HTTP 服务）。</summary>
public sealed class CustomTtsModel : Observable
{
    private string _name = "";
    private string _mode = "cli";
    private string _command = "";
    private string _bodyTemplate = "{\"text\":\"{text}\",\"voice\":\"{voice}\",\"speed\":{speed}}";
    private string _voiceList = "default";
    private string _license = "";
    private string _notes = "";
    private string _workDir = "";

    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get => _name; set => Set(ref _name, value); }

    /// <summary>cli = 调用本地命令；http = POST 到推理服务。</summary>
    public string Mode { get => _mode; set { if (Set(ref _mode, value)) Raise(nameof(ModeText)); } }
    public string Command { get => _command; set => Set(ref _command, value); }
    public string BodyTemplate { get => _bodyTemplate; set => Set(ref _bodyTemplate, value); }
    public string VoiceList { get => _voiceList; set { if (Set(ref _voiceList, value)) Raise(nameof(VoiceNames)); } }
    public string License { get => _license; set => Set(ref _license, value); }
    public string Notes { get => _notes; set => Set(ref _notes, value); }
    public string WorkDir { get => _workDir; set => Set(ref _workDir, value); }

    /// <summary>可选：一键启动本地服务的命令；留空则不托管。</summary>
    public string StartCommand { get; set; } = "";
    /// <summary>健康检查地址，如 http://127.0.0.1:9880/health</summary>
    public string HealthUrl { get; set; } = "";

    public string ModeText => Mode == "http" ? "HTTP 服务" : "本地命令行";
    public string[] VoiceNames => VoiceList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    public string Summary => Mode == "http" ? Command : Path.GetFileName(Command);
}
