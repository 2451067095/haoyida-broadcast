using System.Diagnostics;
using System.IO;
using System.Net.Http;

namespace Haoyida.Core;

/// <summary>
/// 本地模型服务托管：按模型配置的启动命令拉起进程，轮询健康检查地址，
/// 就绪后第三方引擎自动可用；退出软件时统一收掉子进程。
/// </summary>
public static class LocalServiceManager
{
    private sealed class Instance
    {
        public Process? Process;
        public string Status = "未启动";
        public bool Ready;
    }

    private static readonly Dictionary<string, Instance> Running = new();
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(2) };

    public static string StatusOf(string modelId) =>
        Running.TryGetValue(modelId, out var i) ? i.Status : "未启动";

    public static bool IsReady(string modelId) =>
        Running.TryGetValue(modelId, out var i) && i.Ready;

    public static async Task<(bool Ok, string Message)> StartAsync(CustomTtsModel model)
    {
        if (string.IsNullOrWhiteSpace(model.StartCommand))
            return (false, "没有配置启动命令");

        if (Running.TryGetValue(model.Id, out var existing) && existing.Ready)
            return (true, "已在运行");

        var instance = new Instance { Status = "启动中…" };
        Running[model.Id] = instance;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c " + model.StartCommand,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = string.IsNullOrWhiteSpace(model.WorkDir) || !Directory.Exists(model.WorkDir)
                    ? AppContext.BaseDirectory
                    : model.WorkDir
            };

            var process = new Process { StartInfo = psi };
            process.OutputDataReceived += (_, _) => { };
            process.ErrorDataReceived += (_, _) => { };
            if (!process.Start()) return (false, "无法启动进程");

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            instance.Process = process;

            // 最多等 90 秒：大模型加载慢
            for (int i = 0; i < 90; i++)
            {
                await Task.Delay(1000);
                if (process.HasExited)
                {
                    instance.Status = "启动失败（进程已退出）";
                    instance.Ready = false;
                    return (false, instance.Status);
                }

                if (await ProbeAsync(model.HealthUrl))
                {
                    instance.Ready = true;
                    instance.Status = "服务运行中";
                    return (true, instance.Status);
                }

                instance.Status = $"启动中… {i + 1}s";
            }

            instance.Status = "启动超时";
            return (false, instance.Status);
        }
        catch (Exception ex)
        {
            instance.Status = "启动异常：" + ex.Message;
            instance.Ready = false;
            return (false, instance.Status);
        }
    }

    public static void Stop(string modelId)
    {
        if (!Running.TryGetValue(modelId, out var instance)) return;
        try
        {
            if (instance.Process is { HasExited: false })
                instance.Process.Kill(entireProcessTree: true);
        }
        catch { }
        instance.Ready = false;
        instance.Status = "已停止";
    }

    public static void StopAll()
    {
        foreach (var id in Running.Keys.ToList()) Stop(id);
    }

    private static async Task<bool> ProbeAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return true;
        try
        {
            using var response = await Http.GetAsync(url).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }
}
