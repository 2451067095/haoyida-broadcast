using System.IO;
using System.Windows.Media;

namespace Haoyida.Core;

/// <summary>
/// 双通道播放器：人声通道 + 背景音乐通道，人声出现时自动压低 BGM（闪避）。
/// 只使用 WPF 内置 MediaPlayer，不引入 NAudio 等第三方库。
/// </summary>
public sealed class AudioPlayer : IDisposable
{
    private readonly MediaPlayer _voice = new();
    private readonly MediaPlayer _bgm = new();
    private readonly object _gate = new();

    private string _bgmPath = "";
    private bool _bgmPlaying;
    private double _bgmBaseVolume = 0.3;
    private bool _ducked;

    public double MasterVolume { get; set; } = 0.85;

    public event Action? BgmFailed;
    /// <summary>单曲播完（BgmLoop=false 时触发，用于播放列表自动下一首）。</summary>
    public event Action? BgmEnded;
    /// <summary>true=单曲循环；false=播完抛 BgmEnded 交给播放列表。</summary>
    public bool BgmLoop { get; set; } = true;

    public bool IsBgmPlaying => _bgmPlaying;

    public void StartBgm(string path, double volume)
    {
        _bgmBaseVolume = Math.Clamp(volume, 0, 1);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            StopBgm();
            return;
        }

        lock (_gate)
        {
            if (_bgmPlaying && string.Equals(_bgmPath, path, StringComparison.OrdinalIgnoreCase))
            {
                _bgm.Volume = CurrentBgmVolume();
                return;
            }

            try
            {
                _bgmPath = path;
                _bgm.MediaEnded -= OnBgmEnded;
                _bgm.MediaFailed -= OnBgmFailed;
                _bgm.MediaEnded += OnBgmEnded;
                _bgm.MediaFailed += OnBgmFailed;
                _bgm.Open(new Uri(path, UriKind.Absolute));
                _bgm.Volume = CurrentBgmVolume();
                _bgm.Play();
                _bgmPlaying = true;
            }
            catch
            {
                _bgmPlaying = false;
                BgmFailed?.Invoke();
            }
        }
    }

    private void OnBgmFailed(object? sender, ExceptionEventArgs e)
    {
        _bgmPlaying = false;
        BgmFailed?.Invoke();
    }

    private void OnBgmEnded(object? sender, EventArgs e)
    {
        if (!_bgmPlaying) return;
        if (!BgmLoop)
        {
            _bgmPlaying = false;
            BgmEnded?.Invoke();
            return;
        }
        try
        {
            _bgm.Position = TimeSpan.Zero;
            _bgm.Play();
        }
        catch { _bgmPlaying = false; }
    }

    public void StopBgm()
    {
        lock (_gate)
        {
            _bgmPlaying = false;
            _bgmPath = "";
            try { _bgm.Stop(); } catch { }
        }
    }

    public void SetBgmVolume(double volume)
    {
        _bgmBaseVolume = Math.Clamp(volume, 0, 1);
        if (_bgmPlaying) _bgm.Volume = CurrentBgmVolume();
    }

    public void Duck(bool on)
    {
        _ducked = on;
        if (_bgmPlaying)
        {
            try { _bgm.Volume = CurrentBgmVolume(); } catch { }
        }
    }

    private double CurrentBgmVolume() =>
        Math.Clamp(_bgmBaseVolume * (_ducked ? 0.22 : 1.0), 0, 1);

    [System.Runtime.InteropServices.DllImport("winmm.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int mciSendString(string command, System.Text.StringBuilder? ret, int retLength, IntPtr hwnd);

    /// <summary>用 Windows MCI 播放：wav/mp3 都能放，阻塞等待播完，避免 MediaPlayer 静默失败。</summary>
    public Task<TimeSpan> PlayVoiceAsync(string path, double volume, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var alias = "hy" + Guid.NewGuid().ToString("N")[..8];
            try
            {
                var open = mciSendString($"open \"{path}\" type mpegvideo alias {alias}", null, 0, IntPtr.Zero);
                if (open != 0)
                {
                    // 退回 waveaudio 再试一次（纯 PCM 文件）
                    open = mciSendString($"open \"{path}\" type waveaudio alias {alias}", null, 0, IntPtr.Zero);
                    if (open != 0) throw new InvalidOperationException($"无法打开音频（MCI {open}）：{path}");
                }

                mciSendString($"setaudio {alias} volume to {(int)Math.Clamp(volume * MasterVolume * 1000, 0, 1000)}", null, 0, IntPtr.Zero);
                mciSendString($"play {alias} wait", null, 0, IntPtr.Zero);
                return sw.Elapsed;
            }
            finally
            {
                mciSendString($"close {alias}", null, 0, IntPtr.Zero);
            }
        }, ct);
    }
    public void StopVoice()
    {
        try { _voice.Stop(); } catch { }
    }

    public void Dispose()
    {
        try { _voice.Close(); } catch { }
        try { _bgm.Close(); } catch { }
    }
}
