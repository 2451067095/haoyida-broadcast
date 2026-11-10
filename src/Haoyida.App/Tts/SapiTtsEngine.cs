using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Haoyida.Tts;

/// <summary>
/// Windows 内置 SAPI5 引擎（离线、零依赖）。
/// 通过 COM 晚绑定调用，因此不需要 System.Speech NuGet 包。
/// </summary>
public sealed class SapiTtsEngine : ITtsEngine
{
    private const int SsfmCreateForWrite = 3;
    private const int SvsFlagsIsXml = 8;

    public string Id => "sapi";
    public string Name => "Windows 内置语音";
    public string Vendor => "Microsoft SAPI5";
    public string License => "随 Windows 授权，本机可用";
    public string Kind => "离线";
    public string Note => "零依赖兜底引擎；可用音色取决于系统已安装的语音包。";

    private IReadOnlyList<VoiceOption>? _voices;

    public IReadOnlyList<VoiceOption> Voices => _voices ??= Probe();

    public bool IsAvailable(out string reason)
    {
        reason = "";
        try
        {
            var t = Type.GetTypeFromProgID("SAPI.SpVoice");
            if (t is null)
            {
                reason = "系统未注册 SAPI.SpVoice";
                return false;
            }
            if (Voices.Count == 0)
            {
                reason = "系统没有安装任何语音";
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            reason = ex.Message;
            return false;
        }
    }

    private static IReadOnlyList<VoiceOption> Probe()
    {
        var list = new List<VoiceOption>();
        try
        {
            var type = Type.GetTypeFromProgID("SAPI.SpVoice");
            if (type is null) return list;

            dynamic voice = Activator.CreateInstance(type)!;
            dynamic tokens = voice.GetVoices();
            int count = tokens.Count;
            for (int i = 0; i < count; i++)
            {
                dynamic token = tokens.Item(i);
                string desc = token.GetDescription();
                bool zh = desc.Contains("Chinese", StringComparison.OrdinalIgnoreCase)
                          || desc.Contains("中文") || desc.Contains("Huihui") || desc.Contains("Yaoyao")
                          || desc.Contains("Kangkang") || desc.Contains("Xiaoxiao");
                list.Add(new VoiceOption(
                    Id: "sapi:" + i,
                    Name: ShortName(desc),
                    EngineId: "sapi",
                    Gender: desc.Contains("Female") || desc.Contains("Huihui") || desc.Contains("Yaoyao") ? "女声" : "男声",
                    Style: zh ? "合成音" : "英文",
                    Description: desc,
                    SampleText: "欢迎光临，今日特惠，红富士苹果每斤四块九毛八。"));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("枚举 SAPI 音色失败：" + ex.Message);
        }
        return list;
    }

    private static string ShortName(string desc)
    {
        int dash = desc.IndexOf(" - ", StringComparison.Ordinal);
        var name = dash > 0 ? desc[..dash] : desc;
        return name.Replace("Microsoft ", "").Replace(" Desktop", "").Trim();
    }

    public Task<string> SynthesizeAsync(string text, VoiceOption voice, double rate, double pitch, string outputPath, CancellationToken ct)
    {
        // SAPI 是 STA 组件：在专用 STA 线程上调用，避免线程池线程上的 COM 异常。
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    SpeakOnce(text, voice, rate, outputPath, ct);
                    tcs.TrySetResult(outputPath);
                    return;
                }
                catch (ArgumentException) when (attempt == 0)
                {
                    Thread.Sleep(200); // 换一个新的 COM 实例再试一次
                }
            }
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        return tcs.Task;
    }

    private static void SpeakOnce(string text, VoiceOption voice, double rate, string outputPath, CancellationToken ct)
    {
            var sapiType = Type.GetTypeFromProgID("SAPI.SpVoice") ?? throw new InvalidOperationException("SAPI 不可用");
            dynamic speaker = Activator.CreateInstance(sapiType)!;

            int index = 0;
            if (voice.Id.StartsWith("sapi:", StringComparison.Ordinal))
                int.TryParse(voice.Id.AsSpan(5), out index);

            dynamic tokens = speaker.GetVoices();
            if (index >= 0 && index < tokens.Count) speaker.Voice = tokens.Item(index);
            speaker.Rate = (int)Math.Round(Math.Clamp((rate - 1.0) * 10.0, -10, 10));
            speaker.Volume = 100;

            var streamType = Type.GetTypeFromProgID("SAPI.SpFileStream")
                             ?? throw new InvalidOperationException("SAPI.SpFileStream 不可用");
            dynamic stream = Activator.CreateInstance(streamType)!;

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            stream.Open(outputPath, SsfmCreateForWrite, false);
            speaker.AudioOutputStream = stream;

            try
            {
                ct.ThrowIfCancellationRequested();
                speaker.Speak(text, 0);
            }
            finally
            {
                try { stream.Close(); } catch { }
                try { Marshal.ReleaseComObject(stream); } catch { }
                try { Marshal.ReleaseComObject(speaker); } catch { }
            }
    }

    private static string BuildSsml(string text, double pitch)
    {
        int percent = (int)Math.Round((pitch - 1.0) * 100);
        string sign = percent >= 0 ? "+" : "";
        var escaped = text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        var sb = new StringBuilder();
        sb.Append("<speak version=\"1.0\" xmlns=\"http://www.w3.org/2001/10/synthesis\" xml:lang=\"zh-CN\">");
        sb.Append("<prosody pitch=\"").Append(sign).Append(percent).Append("%\">");
        sb.Append(escaped);
        sb.Append("</prosody></speak>");
        return sb.ToString();
    }
}
