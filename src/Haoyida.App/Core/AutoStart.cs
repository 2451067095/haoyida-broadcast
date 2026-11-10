using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Haoyida.Core;

/// <summary>开机自启：写 HKCU\...\Run，无需管理员权限。</summary>
public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "HaoyidaBroadcast";

    public static string ExecutablePath
    {
        get
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe) && exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return exe;
            var local = Path.Combine(AppContext.BaseDirectory, "Haoyida.exe");
            return File.Exists(local) ? local : exe ?? "";
        }
    }

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            var value = key?.GetValue(ValueName) as string;
            return !string.IsNullOrWhiteSpace(value);
        }
        catch { return false; }
    }

    public static bool Apply(bool enabled, out string message)
    {
        message = "";
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKey);

            if (enabled)
            {
                var exe = ExecutablePath;
                if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
                {
                    message = "找不到程序路径，无法设置开机自启";
                    return false;
                }
                key.SetValue(ValueName, $"\"{exe}\" --autostart", RegistryValueKind.String);
                message = "已加入开机启动";
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                message = "已取消开机启动";
            }
            return true;
        }
        catch (Exception ex)
        {
            message = "设置失败：" + ex.Message;
            return false;
        }
    }

    /// <summary>启动时按配置自愈一次，避免注册表被清理后状态不一致。</summary>
    public static void Sync(bool wanted)
    {
        if (wanted == IsEnabled()) return;
        Apply(wanted, out _);
    }
}
