using System.IO;
using System.Text;
using System.Windows;

namespace Haoyida;

/// <summary>带异常落盘的入口：WPF 的 XAML 解析错误默认只会静默崩溃。</summary>
public static class Program
{
    private static string? _logPath;

    public static string LogPath
    {
        get
        {
            if (_logPath is not null) return _logPath;

            var explicitPath = Environment.GetEnvironmentVariable("HAOYIDA_LOG");
            if (!string.IsNullOrWhiteSpace(explicitPath))
            {
                _logPath = explicitPath;
                return _logPath;
            }

            var candidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Haoyida", "startup.log"),
                Path.Combine(AppContext.BaseDirectory, "startup-error.log"),
                @"C:\ProgramData\Haoyida\startup.log"
            };
            _logPath = candidates[0];
            return _logPath;
        }
    }

    /// <summary>无界面端到端语音自检：合成 + 真实播放，结果写日志。</summary>
    private static async Task RunVoiceTest()
    {
        try
        {
            var store = new Haoyida.Core.DataStore();
            var tts = new Haoyida.Tts.TtsService(store.CacheDirectory);
            var profile = store.Data.Store;
            Trace($"voice-test start engine={profile.DefaultEngineId} voice={profile.DefaultVoiceId}");

            foreach (var id in new[] { profile.DefaultEngineId, "kouro", "kokoro", "piper", "sapi" })
            {
                if (string.IsNullOrWhiteSpace(id)) continue;
                try
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var r = await tts.SynthAsync(new Haoyida.Tts.TtsRequest(
                        "欢迎光临，本店红富士苹果每斤四块九毛八。", id, "", 1.0, 1.0), CancellationToken.None);
                    Trace($"synth [{id}] -> {r.EngineId}/{r.VoiceId} {new FileInfo(r.FilePath).Length} bytes in {sw.ElapsedMilliseconds}ms");

                    var player = new Haoyida.Core.AudioPlayer();
                    var sw2 = System.Diagnostics.Stopwatch.StartNew();
                    var played = await player.PlayVoiceAsync(r.FilePath, 1.0, CancellationToken.None);
                    Trace($"play  [{id}] ok {played.TotalSeconds:0.00}s (wall {sw2.ElapsedMilliseconds}ms)");
                }
                catch (Exception ex)
                {
                    Trace($"FAIL  [{id}] {ex.GetType().Name}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Dump("voice-test", ex);
        }
    }
    public static void Trace(string message)
    {
        try
        {
            File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}", Encoding.UTF8);
        }
        catch { }
    }

    [STAThread]
    public static void Main(string[] args)
    {
        bool headless = args.Any(a => a.StartsWith("--shots", StringComparison.OrdinalIgnoreCase));
        Trace("=== start " + string.Join(' ', args));

        if (args.Any(a => a.Equals("--test-excel", StringComparison.OrdinalIgnoreCase)))
        {
            var p = Path.Combine(Path.GetTempPath(), "hy-template.xlsx");
            Haoyida.Core.ExcelKit.WriteTemplate(p, new[]
            {
                new[] { "红富士苹果", "水果", "斤", "6.98", "4.98", "脆甜多汁" },
                new[] { "鲜鸡蛋", "蛋品", "斤", "6.80", "5.28", "" }
            });
            Trace("excel wrote " + new FileInfo(p).Length + " bytes");
            foreach (var row in Haoyida.Core.ExcelKit.ReadRows(p))
                Trace("row: " + string.Join(" | ", row));
            return;
        }
        if (args.Any(a => a.Equals("--test-voice", StringComparison.OrdinalIgnoreCase)))
        {
            RunVoiceTest().GetAwaiter().GetResult();
            return;
        }
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Dump("AppDomain", e.ExceptionObject as Exception);

        try
        {
            Trace("creating App");
            var app = new App();
            Trace("App created; InitializeComponent");
            app.InitializeComponent();
            Trace("InitializeComponent done; Run");
            app.Run();
            Trace("Run returned");
        }
        catch (Exception ex)
        {
            Dump("Main", ex);
            if (!headless)
            {
                MessageBox.Show("启动失败：\n\n" + Flatten(ex) + "\n\n日志：" + LogPath,
                    "耗易达-超市播音软件", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            Environment.Exit(2);
        }
    }

    public static void Dump(string source, Exception? ex)
    {
        if (ex is null) return;
        try
        {
            File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss}] {source} EXCEPTION\n{ex}\n\n", Encoding.UTF8);
        }
        catch { }
    }

    public static string Flatten(Exception ex)
    {
        var text = ex.Message;
        var inner = ex.InnerException;
        int depth = 0;
        while (inner is not null && depth++ < 5)
        {
            text += "\n→ " + inner.Message;
            inner = inner.InnerException;
        }
        return text;
    }
}
