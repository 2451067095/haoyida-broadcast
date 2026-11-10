using System.IO;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Haoyida.Tts;

/// <summary>把请求交给合适的引擎，并做磁盘缓存：同一句话第二次播报是瞬时的。</summary>
public sealed class TtsService : IDisposable
{
    private readonly string _cacheDirectory;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private readonly SapiTtsEngine _sapi;

    public List<ITtsEngine> Engines { get; }
    public TtsService? FallbackEngine => null;

    public TtsService(string cacheDirectory)
    {
        _cacheDirectory = cacheDirectory;
        Directory.CreateDirectory(_cacheDirectory);

        var piperRoot = AppPaths.PiperRoot;
        var piper = new PiperTtsEngine(piperRoot);
        // 在线引擎（Edge）在国内网络被 403 拦截且属非免费服务，按产品策略不启用。
        _sapi = new SapiTtsEngine();
        // sherpa-onnx 多说话人引擎仍在验证（模型输出采样率异常），默认不注册，避免拖慢播报。
        // 需要试用时打开下一行并把 sherpa 放回列表。
        var sherpa = new SherpaTtsEngine(AppPaths.PythonExecutable, AppPaths.SherpaWorkerScript);
        _ = sherpa;

        // 离线优先：网络不可靠时不能让门店的播报卡在在线引擎上。
        var kokoro = new KokoroTtsEngine(AppPaths.PythonExecutable, AppPaths.KokoroWorkerScript);

        Engines = new List<ITtsEngine> { piper, kokoro, _sapi };
    }

    /// <summary>安装/移除第三方模型后重建引擎列表（保留内置引擎）。</summary>
    public void ReloadCustomEngines(IEnumerable<Core.CustomTtsModel> models)
    {
        var rebuilt = Engines.Where(e => !e.Id.StartsWith("ext:", StringComparison.Ordinal)).ToList();
        foreach (var m in models) rebuilt.Add(new ExternalTtsEngine(m));
        Engines.Clear();
        Engines.AddRange(rebuilt);
    }

    public ITtsEngine Engine(string? id)
    {
        var engine = Engines.FirstOrDefault(e => e.Id == id);
        if (engine is not null && engine.IsAvailable(out _)) return engine;

        foreach (var candidate in Engines)
            if (candidate.IsAvailable(out _)) return candidate;

        return _sapi;
    }

    public IReadOnlyList<VoiceOption> AllVoices() =>
        Engines.Where(e => e.IsAvailable(out _)).SelectMany(e => e.Voices).ToList();

    public VoiceOption Voice(string engineId, string? voiceId)
    {
        var engine = Engine(engineId);
        var voices = engine.Voices;
        if (voices.Count == 0)
            return new VoiceOption("none", "无可用音色", engine.Id, "—", "—", "", "");

        return voices.FirstOrDefault(v => v.Id == voiceId) ?? voices[0];
    }

    public async Task<TtsResult> SynthAsync(TtsRequest request, CancellationToken ct)
    {
        var engine = Engine(request.EngineId);
        var voice = Voice(engine.Id, string.IsNullOrEmpty(request.VoiceId) ? null : request.VoiceId);

        var ext = engine.Id switch { "edge" => ".mp3", _ => ".wav" };
        var key = CacheKey(request, voice.Id);
        var path = Path.Combine(_cacheDirectory, key + ext);

        if (File.Exists(path) && new FileInfo(path).Length > 1000)
            return new TtsResult(path, engine.Id, voice.Id, true, 0);

        var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (File.Exists(path) && new FileInfo(path).Length > 1000)
                return new TtsResult(path, engine.Id, voice.Id, true, 0);

            var sw = Stopwatch.StartNew();
            var temp = path + ".part";
            try
            {
                await engine.SynthesizeAsync(request.Text, voice, request.Rate, request.Pitch, temp, ct).ConfigureAwait(false);
                MoveWithRetry(temp, path);
                sw.Stop();
                return new TtsResult(path, engine.Id, voice.Id, false, sw.ElapsedMilliseconds);
            }
            catch (Exception ex) when (engine.Id != "sapi" && !ct.IsCancellationRequested)
            {
                // 在线引擎失败不阻断门店营业：自动回落到本机 SAPI。
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                Debug.WriteLine($"引擎 {engine.Id} 失败，回落 SAPI：{ex.Message}");
                try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "haoyida-tts.log"), $"[{DateTime.Now:HH:mm:ss}] {engine.Id} -> {ex}{Environment.NewLine}"); } catch { }

                if (!_sapi.IsAvailable(out _)) throw new InvalidOperationException("所有语音引擎均不可用：" + ex.Message, ex);

                var fallbackVoice = _sapi.Voices.FirstOrDefault(v => v.Description.Contains("Chinese", StringComparison.OrdinalIgnoreCase))
                                    ?? _sapi.Voices[0];
                var fallbackPath = Path.Combine(_cacheDirectory, CacheKey(request, fallbackVoice.Id) + ".wav");
                var temp2 = fallbackPath + ".part";
                sw.Restart();
                await _sapi.SynthesizeAsync(request.Text, fallbackVoice, request.Rate, 1.0, temp2, ct).ConfigureAwait(false);
                MoveWithRetry(temp2, fallbackPath);
                sw.Stop();
                return new TtsResult(fallbackPath, "sapi", fallbackVoice.Id, false, sw.ElapsedMilliseconds);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>子进程刚关闭文件时 Windows 可能还短暂占用，重试几次再放弃。</summary>
    private static void MoveWithRetry(string source, string target, int attempts = 6)
    {
        for (int i = 0; i < attempts; i++)
        {
            try
            {
                if (File.Exists(target)) File.Delete(target);
                File.Move(source, target);
                return;
            }
            catch (IOException) when (i < attempts - 1)
            {
                Thread.Sleep(80);
            }
        }
        File.Move(source, target, overwrite: true);
    }

    private static string CacheKey(TtsRequest r, string voiceId)
    {
        var raw = string.Join('|', r.EngineId, voiceId, r.Rate.ToString("0.###"), r.Pitch.ToString("0.###"), r.Text);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash)[..20].ToLowerInvariant();
    }

    public (int Files, long Bytes) CacheUsage()
    {
        try
        {
            var files = Directory.GetFiles(_cacheDirectory);
            return (files.Length, files.Sum(f => new FileInfo(f).Length));
        }
        catch { return (0, 0); }
    }

    /// <summary>删除超过 N 天未使用的缓存文件。</summary>
    public int ClearCacheOlderThan(int days)
    {
        int removed = 0;
        try
        {
            var limit = DateTime.Now.AddDays(-Math.Max(1, days));
            foreach (var file in Directory.GetFiles(_cacheDirectory))
            {
                var info = new FileInfo(file);
                if (info.LastAccessTime >= limit) continue;
                try { info.Delete(); removed++; } catch { }
            }
        }
        catch { }
        return removed;
    }

    public void ClearCache()
    {
        try
        {
            foreach (var f in Directory.GetFiles(_cacheDirectory))
                File.Delete(f);
        }
        catch { }
    }

    public void Dispose() => _locks.Clear();
}

/// <summary>程序用到的外部路径解析：开发态和发布态都能找到 piper / python。</summary>
public static class AppPaths
{
    public static string BaseDirectory => AppContext.BaseDirectory;

    /// <summary>
    /// 纯 ASCII 的暂存目录。Piper 是 C++ 程序，遇到中文路径会直接崩溃（0xC0000409），
    /// 所以凡是要交给 piper.exe 的路径都必须落在这里。
    /// </summary>
    public static string AsciiScratchDirectory
    {
        get
        {
            var candidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Haoyida", "scratch"),
                @"C:\ProgramData\Haoyida\scratch",
                Path.Combine(Path.GetTempPath(), "haoyida-scratch")
            };

            foreach (var c in candidates)
            {
                try
                {
                    if (!IsAscii(c)) continue;
                    Directory.CreateDirectory(c);
                    return c;
                }
                catch { }
            }
            return Path.GetTempPath();
        }
    }

    public static bool IsAscii(string path) => path.All(ch => ch < 128);

    private static string? _resolvedPiper;

    public static string PiperRoot
    {
        get
        {
            if (_resolvedPiper is not null) return _resolvedPiper;

            var env = Environment.GetEnvironmentVariable("HAOYIDA_PIPER_DIR");
            if (!string.IsNullOrWhiteSpace(env) && File.Exists(Path.Combine(env, "piper.exe")))
                return _resolvedPiper = EnsureAsciiRuntime(env);

            // 从程序目录逐级向上找 piper\ 或 _vendor\piper，开发态/发布态都能命中。
            var probes = new List<string> { Path.Combine(BaseDirectory, "piper") };
            var dir = new DirectoryInfo(BaseDirectory);
            for (int i = 0; i < 8 && dir is not null; i++)
            {
                probes.Add(Path.Combine(dir.FullName, "piper"));
                probes.Add(Path.Combine(dir.FullName, "_vendor", "piper"));
                dir = dir.Parent;
            }
            probes.Add(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Haoyida", "runtime", "piper"));

            foreach (var c in probes)
            {
                var full = Path.GetFullPath(c);
                if (File.Exists(Path.Combine(full, "piper.exe")))
                    return _resolvedPiper = EnsureAsciiRuntime(full);
            }

            return _resolvedPiper = Path.GetFullPath(probes[0]);
        }
    }

    /// <summary>如果 piper 落在中文路径下，先整目录搬到 C:\ProgramData\Haoyida\runtime\piper。</summary>
    private static string EnsureAsciiRuntime(string source)
    {
        if (IsAscii(source)) return source;

        try
        {
            var target = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Haoyida", "runtime", "piper");

            if (!IsAscii(target)) target = @"C:\ProgramData\Haoyida\runtime\piper";

            var stamp = Path.Combine(target, ".stamp");
            var sourceExe = new FileInfo(Path.Combine(source, "piper.exe"));
            var needCopy = !File.Exists(Path.Combine(target, "piper.exe"));

            if (!needCopy && File.Exists(stamp))
            {
                var recorded = File.ReadAllText(stamp).Trim();
                needCopy = recorded != $"{sourceExe.Length}:{sourceExe.LastWriteTimeUtc.Ticks}";
            }

            if (needCopy)
            {
                Directory.CreateDirectory(target);
                CopyDirectory(source, target, skip: "piper_windows_amd64.zip");
                File.WriteAllText(stamp, $"{sourceExe.Length}:{sourceExe.LastWriteTimeUtc.Ticks}");
            }

            return target;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("搬运 Piper 运行时失败：" + ex.Message);
            return source;
        }
    }

    private static void CopyDirectory(string source, string target, string skip)
    {
        foreach (var dir in Directory.GetDirectories(source))
        {
            var name = Path.GetFileName(dir);
            Directory.CreateDirectory(Path.Combine(target, name));
            CopyDirectory(dir, Path.Combine(target, name), skip);
        }

        foreach (var file in Directory.GetFiles(source))
        {
            if (string.Equals(Path.GetFileName(file), skip, StringComparison.OrdinalIgnoreCase)) continue;
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
        }
    }

    private static string? _resolvedSherpa;

    public static string SherpaRoot
    {
        get
        {
            if (_resolvedSherpa is not null) return _resolvedSherpa;

            var env = Environment.GetEnvironmentVariable("HAOYIDA_SHERPA_DIR");
            if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env))
                return _resolvedSherpa = EnsureAsciiSherpa(env);

            var probes = new List<string> { Path.Combine(BaseDirectory, "sherpa") };
            var dir = new DirectoryInfo(BaseDirectory);
            for (int i = 0; i < 8 && dir is not null; i++)
            {
                probes.Add(Path.Combine(dir.FullName, "sherpa"));
                probes.Add(Path.Combine(dir.FullName, "sherpa", "vits-zh-aishell3"));
                probes.Add(Path.Combine(dir.FullName, "_vendor", "sherpa", "vits-zh-aishell3"));
                dir = dir.Parent;
            }

            foreach (var c in probes)
            {
                if (File.Exists(Path.Combine(c, "vits-aishell3.onnx")))
                    return _resolvedSherpa = EnsureAsciiSherpa(c);
            }
            return _resolvedSherpa = probes[0];
        }
    }

    public static string SherpaWorkerScript =>
        Path.Combine(BaseDirectory, "Assets", "tts", "sherpa_tts_worker.py");

    private static string EnsureAsciiSherpa(string source)
    {
        if (IsAscii(source)) return source;
        try
        {
            var target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Haoyida", "runtime", "sherpa");
            if (!IsAscii(target)) target = @"C:\ProgramData\Haoyida\runtime\sherpa";

            Directory.CreateDirectory(target);
            foreach (var file in Directory.GetFiles(source))
            {
                var dest = Path.Combine(target, Path.GetFileName(file));
                if (!File.Exists(dest) || new FileInfo(dest).Length != new FileInfo(file).Length)
                    File.Copy(file, dest, overwrite: true);
            }
            return target;
        }
        catch { return source; }
    }

    private static string? _resolvedKokoro;

    public static string KokoroRoot
    {
        get
        {
            if (_resolvedKokoro is not null) return _resolvedKokoro;
            var probes = new List<string> { Path.Combine(BaseDirectory, "kokoro-zh") };
            var dir = new DirectoryInfo(BaseDirectory);
            for (int i = 0; i < 8 && dir is not null; i++)
            {
                probes.Add(Path.Combine(dir.FullName, "kokoro-zh"));
                probes.Add(Path.Combine(dir.FullName, "_vendor", "kokoro-zh"));
                dir = dir.Parent;
            }
            foreach (var p in probes)
                if (File.Exists(Path.Combine(p, "tokenizer.json")))
                {
                    var resolved = EnsureAsciiKokoro(p);
                    if (!string.Equals(resolved, p, StringComparison.OrdinalIgnoreCase))
                        EnsureAsciiKokoro(p); // 补拷后来新增的音色
                    return _resolvedKokoro = resolved;
                }
            return _resolvedKokoro = probes[0];
        }
    }

    public static string KokoroWorkerScript => Path.Combine(BaseDirectory, "Assets", "tts", "kokoro_worker.py");

    private static string EnsureAsciiKokoro(string source)
    {
        if (IsAscii(source)) return source;
        try
        {
            var target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Haoyida", "runtime", "kokoro-zh");
            if (!IsAscii(target)) target = @"C:\ProgramData\Haoyida\runtime\kokoro-zh";
            foreach (var sub in new[] { "", "onnx", "voices" })
            {
                var src = Path.Combine(source, sub);
                var dst = Path.Combine(target, sub);
                if (!Directory.Exists(src)) continue;
                Directory.CreateDirectory(dst);
                foreach (var file in Directory.GetFiles(src))
                {
                    var to = Path.Combine(dst, Path.GetFileName(file));
                    if (!File.Exists(to) || new FileInfo(to).Length != new FileInfo(file).Length)
                        File.Copy(file, to, overwrite: true);
                }
            }
            return target;
        }
        catch { return source; }
    }

    public static string PythonExecutable
    {
        get
        {
            // 优先使用随包内置的 Python（免安装）
            var bundled = Path.Combine(BaseDirectory, "python", "python.exe");
            if (File.Exists(bundled)) return bundled;

            var env = Environment.GetEnvironmentVariable("HAOYIDA_PYTHON");
            if (!string.IsNullOrWhiteSpace(env) && File.Exists(env)) return env;

            var local = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Programs\Python\Python312\python.exe");
            if (File.Exists(local)) return local;

            return "python";
        }
    }
}
