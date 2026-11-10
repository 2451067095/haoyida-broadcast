using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows;
using System.Windows.Input;

namespace Haoyida.Setup;

public partial class MainWindow : Window
{
    private const string PayloadName = "payload.zip";
    private bool _installing;

    public MainWindow()
    {
        InitializeComponent();
        CheckRuntime();
        PathBox.Text = PickDefaultPath();
    }

    private void Root_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed && !_installing)
        {
            try { DragMove(); } catch { }
        }
    }

    /// <summary>优先选 C 盘以外的固定磁盘（空间充足），避免占用系统盘。</summary>
    private static string PickDefaultPath()
    {
        try
        {
            var best = DriveInfo.GetDrives()
                .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
                .Where(d => !d.Name.StartsWith("C", StringComparison.OrdinalIgnoreCase))
                .Where(d => d.AvailableFreeSpace > 3L * 1024 * 1024 * 1024)
                .OrderByDescending(d => d.AvailableFreeSpace)
                .FirstOrDefault();

            if (best is not null) return Path.Combine(best.Name, "Haoyida");
        }
        catch { }
        return @"C:\Haoyida";
    }

    private void CheckRuntime()
    {
        try
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "shared", "Microsoft.WindowsDesktop.App");
            var ok = Directory.Exists(root) &&
                     Directory.GetDirectories(root).Any(d => Path.GetFileName(d).StartsWith("10."));
            RuntimeHint.Text = ok
                ? "✓ 已检测到 .NET 10 桌面运行时"
                : "⚠ 未检测到 .NET 10 桌面运行时，安装后需另行安装（软件将无法启动）";
            RuntimeHint.Foreground = ok
                ? (System.Windows.Media.Brush)FindResource("Ok")
                : (System.Windows.Media.Brush)FindResource("Danger");
        }
        catch
        {
            RuntimeHint.Text = "运行环境检测失败，可继续安装。";
        }
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "选择安装位置" };
        if (dialog.ShowDialog() == true) PathBox.Text = dialog.FolderName;
    }

    private void OpenDir_Click(object sender, RoutedEventArgs e)
    {
        var path = PathBox.Text.Trim();
        if (Directory.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        var target = PathBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(target))
        {
            Status.Text = "请填写安装位置。";
            return;
        }

        // 先探测目标目录可写，避免装到一半才失败
        try
        {
            Directory.CreateDirectory(target);
            var probe = Path.Combine(target, ".write-test");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
        }
        catch (Exception ex)
        {
            Status.Text = "目标目录不可写";
            MessageBox.Show(
                $"无法写入：{target}\n\n{ex.Message}\n\n" +
                "请换成有写入权限的位置，例如：\n  C:\\Haoyida\n  D:\\Haoyida\n" +
                "（若仍失败，请右键安装程序 →「以管理员身份运行」）",
                "耗易达", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _installing = true;
        InstallBtn.IsEnabled = false;
        CancelBtn.IsEnabled = false;

        // 目标目录里的软件若在运行（占用 debug.log / dll），先关掉再装
        foreach (var proc in Process.GetProcessesByName("Haoyida"))
        {
            try
            {
                if (!proc.HasExited) proc.Kill(entireProcessTree: true);
            }
            catch { }
        }
        await Task.Delay(600);

        try
        {
            await Task.Run(() => Extract(target, p =>
            {
                Dispatcher.Invoke(() =>
                {
                    Bar.Value = p;
                    Status.Text = $"正在安装… {p:0}%";
                });
            }));

            if (DesktopChk.IsChecked == true) CreateShortcut(target);

            Bar.Value = 100;
            Status.Text = "安装完成。";
            Hint.Text = "可以从桌面快捷方式启动。";

            if (LaunchChk.IsChecked == true)
            {
                var exe = Path.Combine(target, "Haoyida.exe");
                if (File.Exists(exe))
                    Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = target });
            }

            MessageBox.Show("安装完成！\n\n位置：" + target, "耗易达", MessageBoxButton.OK, MessageBoxImage.Information);
            Close();
        }
        catch (Exception ex)
        {
            Status.Text = "安装失败：" + ex.Message;
            MessageBox.Show("安装失败：\n" + ex.Message, "耗易达", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _installing = false;
            InstallBtn.IsEnabled = true;
            CancelBtn.IsEnabled = true;
        }
    }

    private static void Extract(string target, Action<double> progress)
    {
        Directory.CreateDirectory(target);

        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(PayloadName, StringComparison.OrdinalIgnoreCase))
                   ?? throw new InvalidOperationException("安装包缺少程序数据（payload.zip）");

        using var stream = asm.GetManifestResourceStream(name)!;
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);

        var entries = zip.Entries.Where(x => !string.IsNullOrEmpty(x.Name)).ToList();
        int done = 0;

        foreach (var entry in entries)
        {
            var dest = Path.GetFullPath(Path.Combine(target, entry.FullName));
            if (!dest.StartsWith(Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                continue; // 防目录穿越

            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    entry.ExtractToFile(dest, overwrite: true);
                    break;
                }
                catch (IOException) when (attempt < 4)
                {
                    Thread.Sleep(250);
                }
                catch (UnauthorizedAccessException) when (attempt < 4)
                {
                    Thread.Sleep(250);
                }
            }

            done++;
            if (done % 20 == 0 || done == entries.Count)
                progress(done * 100.0 / entries.Count);
        }
    }

    private static void CreateShortcut(string target)
    {
        try
        {
            var exe = Path.Combine(target, "Haoyida.exe");
            if (!File.Exists(exe)) return;

            var link = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "耗易达-超市播音软件.lnk");

            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return;
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(link);
            shortcut.TargetPath = exe;
            shortcut.WorkingDirectory = target;
            shortcut.Description = "耗易达-超市播音软件";
            shortcut.Save();
        }
        catch { /* 快捷方式失败不影响安装 */ }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_installing) return;
        Close();
    }
}
