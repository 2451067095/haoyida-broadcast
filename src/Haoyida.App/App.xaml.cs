using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Haoyida.Core;
using Haoyida.Tts;
using Haoyida.ViewModels;

namespace Haoyida;

public partial class App : Application
{
    public static MainViewModel Shell { get; private set; } = null!;
    public static bool Headless { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Headless = e.Args.Any(a => a.StartsWith("--shots", StringComparison.OrdinalIgnoreCase));
        Program.Trace("OnStartup begin");

        var dialogArg = e.Args.FirstOrDefault(a => a.Equals("--shot-dialog", StringComparison.OrdinalIgnoreCase));
        if (dialogArg is not null)
        {
            var outPath = e.Args.Length > Array.IndexOf(e.Args, dialogArg) + 1
                ? e.Args[Array.IndexOf(e.Args, dialogArg) + 1]
                : Path.Combine(AppContext.BaseDirectory, "compliance.png");
            _ = CaptureDialogAsync(outPath);
            return;
        }        var autoArg = e.Args.FirstOrDefault(a => a.Equals("--autostart", StringComparison.OrdinalIgnoreCase));
        if (autoArg is not null)
        {
            var on = e.Args.Length > Array.IndexOf(e.Args, autoArg) + 1 &&
                     e.Args[Array.IndexOf(e.Args, autoArg) + 1].Equals("on", StringComparison.OrdinalIgnoreCase);
            Core.AutoStart.Apply(on, out var msg);
            Program.Trace("autostart " + (on ? "on" : "off") + ": " + msg);
            Shutdown(0);
            return;
        }        var logoArg = e.Args.FirstOrDefault(a => a.Equals("--logo", StringComparison.OrdinalIgnoreCase));
        if (logoArg is not null)
        {
            var outPath = e.Args.Length > Array.IndexOf(e.Args, logoArg) + 1
                ? e.Args[Array.IndexOf(e.Args, logoArg) + 1]
                : Path.Combine(AppContext.BaseDirectory, "app.ico");
            ExportLogo(outPath);
            Program.Trace("logo exported: " + outPath);
            Shutdown(0);
            return;
        }
        try
        {
            BuildShell(e);
        }
        catch (Exception ex)
        {
            Program.Dump("OnStartup", ex);
            if (!Headless) MessageBox.Show("启动失败：" + ex.Message, "耗易达");
            Shutdown(3);
        }
    }

    private void BuildShell(StartupEventArgs e)
    {

        DispatcherUnhandledException += (_, args) =>
        {
            Program.Dump("Dispatcher", args.Exception);
            if (!Headless)
            {
                MessageBox.Show("程序遇到了一个错误：\n\n" + args.Exception.Message + "\n\n日志：" + Program.LogPath,
                    "耗易达-超市播音软件", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            args.Handled = true;
        };

        var store = new DataStore();
        Program.Trace("store ok: " + store.DataDirectory);
        var tts = new TtsService(store.CacheDirectory);
        Program.Trace("tts ok");
        var player = new AudioPlayer();
        var engine = new BroadcastEngine(store, tts, player);
        Core.AutoStart.Sync(store.Data.Store.AutoStart);
        var scheduler = new Scheduler(store);
        var music = new MusicService(store, player);

        Shell = new MainViewModel(store, tts, player, engine, scheduler, music);
        Program.Trace("viewmodel ok");

        var window = new MainWindow();
        MainWindow = window;
        window.DataContext = Shell;
        Program.Trace("window created");
        window.Show();
        Program.Trace("window shown");
        Shell.Start();
        Program.Trace("shell started");

        var shotArg = e.Args.FirstOrDefault(a => a.StartsWith("--shots", StringComparison.OrdinalIgnoreCase));
        if (shotArg is not null)
        {
            var dir = e.Args.Length > Array.IndexOf(e.Args, shotArg) + 1
                ? e.Args[Array.IndexOf(e.Args, shotArg) + 1]
                : Path.Combine(AppContext.BaseDirectory, "shots");
            _ = CaptureAsync(window, dir);
        }

        if (e.Args.Any(a => a.Equals("--test-voice", StringComparison.OrdinalIgnoreCase)))
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(1500);
                try
                {
                    var diag = await Dispatcher.InvokeAsync(() => Shell.DiagnosePreviewAsync());
                    Program.Trace("test-voice: " + diag);
                }
                catch (Exception ex) { Program.Trace("test-voice EX: " + ex.Message); }
                finally { Dispatcher.Invoke(() => Shutdown(0)); }
            });
        }        if (e.Args.Any(a => a.Equals("--selftest", StringComparison.OrdinalIgnoreCase)))
            _ = SelfTestAsync();
    }

    private static async Task CaptureDialogAsync(string outputPath)
    {
        try
        {
            await Task.Delay(600);
            var dlg = new Haoyida.Views.ComplianceWindow();
            dlg.Show();
            await Task.Delay(900);
            var root = (FrameworkElement)dlg.Content;
            root.UpdateLayout();

            var dpi = VisualTreeHelper.GetDpi(root);
            int w = (int)Math.Round(root.ActualWidth * dpi.DpiScaleX);
            int h = (int)Math.Round(root.ActualHeight * dpi.DpiScaleY);
            var bmp = new RenderTargetBitmap(Math.Max(w, 1), Math.Max(h, 1), 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
            bmp.Render(root);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            using var fs = File.Create(outputPath);
            encoder.Save(fs);
        }
        catch (Exception ex)
        {
            Program.Dump("dialog-shot", ex);
        }
        finally
        {
            Current.Shutdown();
        }
    }

    /// <summary>把矢量 Logo 渲染成多尺寸 PNG 并封装为 .ico（PNG 压缩条目，Windows 10+ 原生支持）。</summary>
    private static void ExportLogo(string outputPath)
    {
        int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
        var frames = new List<(int Size, byte[] Data)>();

        foreach (var size in sizes)
        {
            var mark = new Haoyida.Views.LogoMark { Width = size, Height = size };
            mark.Measure(new Size(size, size));
            mark.Arrange(new Rect(0, 0, size, size));
            mark.ApplyTemplate();
            mark.UpdateLayout();

            var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(mark);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            frames.Add((size, ms.ToArray()));
        }

        var icoPath = outputPath.EndsWith(".ico", StringComparison.OrdinalIgnoreCase)
            ? outputPath
            : Path.ChangeExtension(outputPath, ".ico");
        Directory.CreateDirectory(Path.GetDirectoryName(icoPath)!);

        using (var fs = File.Create(icoPath))
        using (var bw = new BinaryWriter(fs))
        {
            bw.Write((ushort)0);
            bw.Write((ushort)1);
            bw.Write((ushort)frames.Count);

            int offset = 6 + 16 * frames.Count;
            foreach (var (size, data) in frames)
            {
                bw.Write((byte)(size >= 256 ? 0 : size));
                bw.Write((byte)(size >= 256 ? 0 : size));
                bw.Write((byte)0);
                bw.Write((byte)0);
                bw.Write((ushort)1);
                bw.Write((ushort)32);
                bw.Write(data.Length);
                bw.Write(offset);
                offset += data.Length;
            }

            foreach (var (_, data) in frames) bw.Write(data);
        }

        File.WriteAllBytes(Path.ChangeExtension(icoPath, ".png"), frames[^1].Data);
    }

    /// <summary>端到端自检：枚举引擎 → 真实合成一句 → 校验产物。用于无人值守验收。</summary>
    private static async Task SelfTestAsync()
    {
        try
        {
            await Task.Delay(600);
            var tts = Shell.Tts;

            foreach (var e2 in tts.Engines)
            {
                var ok = e2.IsAvailable(out var why);
                Program.Trace($"engine {e2.Id} available={ok} voices={e2.Voices.Count} note={why}");
            }

            Program.Trace("price 6.98=" + NumberToChinese.Price(6.98m) + " | 6.90=" + NumberToChinese.Price(6.90m)
                + " | 0.90=" + NumberToChinese.Price(0.90m) + " | 12.00=" + NumberToChinese.Price(12m)
                + " | 6.05=" + NumberToChinese.Price(6.05m) + " | 19.80=" + NumberToChinese.Price(19.80m));

            foreach (var pitchTest in new[] { 0.88, 1.0, 1.12 })
            {
                try
                {
                    var r0 = await tts.SynthAsync(new TtsRequest("欢迎光临，红富士苹果每斤四块九毛八。", "piper", "", 1.0, pitchTest), CancellationToken.None);
                    Program.Trace($"pitch {pitchTest:0.00} -> {WavUtil.DurationSeconds(r0.FilePath):0.00}s bytes={new FileInfo(r0.FilePath).Length}");
                }
                catch (Exception ex)
                {
                    Program.Trace($"pitch {pitchTest:0.00} FAILED: {ex.Message}");
                }
            }

            foreach (var engineId in new[] { "piper", "kokoro", "sapi" })
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    var r = await tts.SynthAsync(
                        new TtsRequest("欢迎光临，红富士苹果每斤四块九毛八。", engineId, "", 1.0, 1.0),
                        CancellationToken.None);
                    var len = new FileInfo(r.FilePath).Length;
                    Program.Trace($"selftest {engineId} -> {r.FilePath} bytes={len} cache={r.FromCache} total={sw.ElapsedMilliseconds}ms actual={r.EngineId}");
                }
                catch (Exception ex)
                {
                    Program.Trace($"selftest {engineId} FAILED: {ex}");
                }
            }
        }
        catch (Exception ex)
        {
            Program.Dump("selftest", ex);
        }
        finally
        {
            Current.Shutdown();
        }
    }

    /// <summary>
    /// 自检截图：把 7 个页面依次渲染成 PNG，用于设计走查（无需人工点界面）。
    /// 用法：Haoyida.exe --shots D:\shots
    /// </summary>
    private static async Task CaptureAsync(Window window, string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            window.Width = 1440;
            window.Height = 900;

            for (int i = 0; i < 8; i++)
            {
                Shell.PageIndex = i;
                await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                await Task.Delay(450);

                var root = (FrameworkElement)window.Content;
                root.UpdateLayout();

                var dpi = VisualTreeHelper.GetDpi(root);
                int w = (int)Math.Round(root.ActualWidth * dpi.DpiScaleX);
                int h = (int)Math.Round(root.ActualHeight * dpi.DpiScaleY);
                if (w <= 0 || h <= 0) { w = 1440; h = 900; }

                var bmp = new RenderTargetBitmap(w, h, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
                bmp.Render(root);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bmp));
                var file = Path.Combine(directory, $"{i + 1:00}-{Shell.PageSlug}.png");
                using var fs = File.Create(file);
                encoder.Save(fs);
            }
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(directory, "capture-error.txt"), ex.ToString());
        }
        finally
        {
            Current.Shutdown();
        }
    }
}
