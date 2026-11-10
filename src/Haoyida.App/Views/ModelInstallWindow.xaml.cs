using System.IO;
using System.Windows;
using System.Windows.Input;
using Haoyida.Core;
using Haoyida.Tts;

namespace Haoyida.Views;

public partial class ModelInstallWindow : Window
{
    private readonly CustomTtsModel _draft;
    private readonly string _dataDir;

    public CustomTtsModel Result => _draft;

    public ModelInstallWindow(CustomTtsModel? model, string dataDir)
    {
        InitializeComponent();
        _dataDir = dataDir;

        _draft = model is null
            ? new CustomTtsModel { Name = "", Mode = "cli" }
            : new CustomTtsModel
            {
                Id = model.Id,
                Name = model.Name,
                Mode = model.Mode,
                Command = model.Command,
                BodyTemplate = model.BodyTemplate,
                VoiceList = model.VoiceList,
                License = model.License,
                WorkDir = model.WorkDir,
                Notes = model.Notes
            };

        NameBox.Text = _draft.Name;
        ModeBox.SelectedIndex = _draft.Mode == "http" ? 1 : 0;
        CommandBox.Text = _draft.Command;
        BodyBox.Text = _draft.BodyTemplate;
        VoiceBox.Text = _draft.VoiceList;
        LicenseBox.Text = _draft.License;
        WorkDirBox.Text = _draft.WorkDir;
        NotesBox.Text = _draft.Notes;
        ApplyMode();
    }

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) { try { DragMove(); } catch { } }
    }

    private void Mode_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => ApplyMode();

    private void ApplyMode()
    {
        if (HttpPanel is null) return;
        bool http = ModeBox.SelectedIndex == 1;
        HttpPanel.Visibility = http ? Visibility.Visible : Visibility.Collapsed;
        CommandLabel.Text = http ? "接口地址" : "命令模板";
        CommandHint.Text = http
            ? "示例：http://127.0.0.1:9880/tts（POST JSON，返回音频字节，或 {\"audio\":\"base64\"}）"
            : "占位符：{text} 文本、{out} 输出 wav 路径、{voice} 音色、{speed} 语速。例：python run_tts.py --text {text} --out {out} --voice {voice}";
    }

    private CustomTtsModel ReadDraft() => new()
    {
        Id = _draft.Id,
        Name = NameBox.Text.Trim(),
        Mode = ModeBox.SelectedIndex == 1 ? "http" : "cli",
        Command = CommandBox.Text.Trim(),
        BodyTemplate = BodyBox.Text.Trim(),
        VoiceList = string.IsNullOrWhiteSpace(VoiceBox.Text) ? "default" : VoiceBox.Text.Trim(),
        License = LicenseBox.Text.Trim(),
        WorkDir = WorkDirBox.Text.Trim(),
        Notes = NotesBox.Text.Trim()
    };

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        var probe = ReadDraft();
        if (string.IsNullOrWhiteSpace(probe.Command))
        {
            StatusText.Text = "请先填写命令或接口地址。";
            return;
        }

        TestBtn.IsEnabled = false;
        StatusText.Foreground = (System.Windows.Media.Brush)FindResource("AccentBrush");
        StatusText.Text = "正在测试，请稍候…";

        var tmpDir = Path.Combine(Path.GetTempPath(), "haoyida-model-test");
        Directory.CreateDirectory(tmpDir);
        var outPath = Path.Combine(tmpDir, "probe.wav");

        try
        {
            var engine = new ExternalTtsEngine(probe);
            var voice = engine.Voices.First();
            await engine.SynthesizeAsync("欢迎光临，本店今日有特惠。", voice, 1.0, 1.0, outPath, CancellationToken.None);
            var size = new FileInfo(outPath).Length;
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("OkBrush");
            StatusText.Text = $"测试成功：产出 {size / 1024.0:0.0} KB 音频。";
        }
        catch (Exception ex)
        {
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("DangerBrush");
            StatusText.Text = "测试失败：" + ex.Message;
        }
        finally
        {
            TestBtn.IsEnabled = true;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var draft = ReadDraft();
        if (string.IsNullOrWhiteSpace(draft.Name))
        {
            StatusText.Text = "请填写模型名称。";
            return;
        }
        if (string.IsNullOrWhiteSpace(draft.Command))
        {
            StatusText.Text = "请填写命令或接口地址。";
            return;
        }

        _draft.Name = draft.Name;
        _draft.Mode = draft.Mode;
        _draft.Command = draft.Command;
        _draft.BodyTemplate = draft.BodyTemplate;
        _draft.VoiceList = draft.VoiceList;
        _draft.License = draft.License;
        _draft.WorkDir = draft.WorkDir;
        _draft.Notes = draft.Notes;

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
