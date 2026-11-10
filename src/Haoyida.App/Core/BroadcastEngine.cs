using System.Collections.ObjectModel;

namespace Haoyida.Core;

/// <summary>播报引擎：队列 + 合成 + 播放 + 闪避 + 日志，一条流水线。</summary>
public sealed class BroadcastEngine
{
    private readonly DataStore _store;
    private readonly Tts.TtsService _tts;
    private readonly AudioPlayer _player;
    private readonly object _gate = new();

    private CancellationTokenSource? _currentCts;
    private bool _pumping;
    private QueueItem? _current;

    public ObservableCollection<QueueItem> Queue { get; } = new();
    public ObservableCollection<LogEntry> Logs { get; } = new();

    public event Action? Changed;
    public event Action<QueueItem>? StateChanged;

    public BroadcastEngine(DataStore store, Tts.TtsService tts, AudioPlayer player)
    {
        _store = store;
        _tts = tts;
        _player = player;
        _player.MasterVolume = store.Data.Store.MasterVolume;

        foreach (var log in _store.Data.Logs.OrderByDescending(l => l.At).Take(200)) Logs.Add(log);
    }

    /// <summary>最近一次合成的错误原因（预览也记录），用于界面诊断。</summary>
    public string LastError { get; private set; } = "";

    public QueueItem? Current
    {
        get => _current;
        private set { _current = value; Changed?.Invoke(); }
    }

    public bool IsSpeaking => _current is { State: QueueState.Speaking or QueueState.Synthesizing };

    public int TodayCount => Logs.Count(l => l.At.Date == DateTime.Today && l.Status == "成功");

    public LogEntry? LastSuccess => Logs.FirstOrDefault(l => l.Status == "成功");

    public void Enqueue(QueueItem item)
    {
        lock (_gate)
        {
            Queue.Add(item);
            Reorder();
        }
        Changed?.Invoke();
        _ = PumpAsync();
    }

    private void Reorder()
    {
        var ordered = Queue.OrderBy(q => q.Priority).ThenBy(q => q.EnqueuedAt).ToList();
        Queue.Clear();
        foreach (var q in ordered) Queue.Add(q);
    }

    public QueueItem FromTask(BroadcastTask task, string text)
    {
        var store = _store.Data.Store;
        return new QueueItem
        {
            Title = string.IsNullOrWhiteSpace(task.Name) ? "定时任务" : task.Name,
            Text = text,
            Priority = task.Priority,
            EngineId = string.IsNullOrEmpty(task.EngineId) ? store.DefaultEngineId : task.EngineId,
            VoiceId = string.IsNullOrEmpty(task.VoiceId) ? store.DefaultVoiceId : task.VoiceId,
            Rate = store.DefaultRate,
            Pitch = store.DefaultPitch,
            Volume = task.Volume,
            Origin = task.Kind switch
            {
                ScheduleKind.Interval => "轮播",
                ScheduleKind.Once => "单次",
                _ => "计划"
            }
        };
    }

    public QueueItem MakeItem(string title, string text, int priority, string origin,
        string? engineId = null, string? voiceId = null, double? rate = null, double? pitch = null)
    {
        var store = _store.Data.Store;
        return new QueueItem
        {
            Title = title,
            Text = text,
            Priority = priority,
            Origin = origin,
            EngineId = string.IsNullOrEmpty(engineId) ? store.DefaultEngineId : engineId,
            VoiceId = string.IsNullOrEmpty(voiceId) ? store.DefaultVoiceId : voiceId,
            Rate = rate ?? store.DefaultRate,
            Pitch = pitch ?? store.DefaultPitch,
            Volume = 1.0
        };
    }

    /// <summary>紧急插播：立刻中断当前播报、清空排队，把新内容顶到最前。</summary>
    public void Interrupt(string title, string text)
    {
        try { _currentCts?.Cancel(); } catch { }

        lock (_gate)
        {
            foreach (var q in Queue.Where(q => q.State is QueueState.Queued).ToList())
            {
                q.State = QueueState.Canceled;
                Queue.Remove(q);
            }
        }

        _player.StopVoice();
        Enqueue(MakeItem(title, text, 0, "插播"));
    }

    public void Cancel(QueueItem item)
    {
        if (item == _current) { try { _currentCts?.Cancel(); } catch { } return; }
        item.State = QueueState.Canceled;
        Queue.Remove(item);
        Changed?.Invoke();
    }

    public void ClearQueue()
    {
        foreach (var q in Queue.ToList())
        {
            if (q.State == QueueState.Queued) q.State = QueueState.Canceled;
        }
        Queue.Clear();
        Changed?.Invoke();
    }

    /// <summary>试听：不入队、不占播报统计，但会写一条“试听”日志。</summary>
    public async Task PreviewAsync(string text, string engineId, string voiceId, double rate, double pitch)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var item = MakeItem("试听", text, 0, "试听", engineId, voiceId, rate, pitch);
        await ProcessAsync(item, isPreview: true).ConfigureAwait(true);
    }

    private async Task PumpAsync()
    {
        if (_pumping) return;
        _pumping = true;
        try
        {
            while (true)
            {
                QueueItem? next;
                lock (_gate)
                {
                    next = Queue.FirstOrDefault(q => q.State == QueueState.Queued);
                    if (next is null) break;
                }

                _currentCts?.Dispose();
                _currentCts = new CancellationTokenSource();
                await ProcessAsync(next, isPreview: false, _currentCts.Token).ConfigureAwait(true);
            }
        }
        finally
        {
            _pumping = false;
            Current = null;
            _player.Duck(false);
            Changed?.Invoke();
        }
    }

    private async Task ProcessAsync(QueueItem item, bool isPreview, CancellationToken ct = default)
    {
        if (!isPreview) Current = item;
        item.StartedAt = DateTime.Now;
        LastError = "";

        var log = new LogEntry
        {
            At = DateTime.Now,
            Title = item.Title,
            Text = item.Text.Length > 60 ? item.Text[..60] + "…" : item.Text
        };

        try
        {
            item.State = QueueState.Synthesizing;
            if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);

            var result = await _tts.SynthAsync(
                new Tts.TtsRequest(item.Text, item.EngineId, item.VoiceId, item.Rate, item.Pitch), ct)
                .ConfigureAwait(true);

            log.Engine = result.EngineId;
            log.Voice = result.VoiceId;

            if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);

            item.State = QueueState.Speaking;
            _player.Duck(true);
            var elapsed = await _player.PlayVoiceAsync(result.FilePath, item.Volume, ct).ConfigureAwait(true);

            item.State = QueueState.Done;
            log.Status = isPreview ? "试听" : "成功";
            log.ElapsedMs = result.FromCache ? 0 : result.ElapsedMs + (long)elapsed.TotalMilliseconds;
            if (result.FromCache) log.ElapsedMs = (long)elapsed.TotalMilliseconds;
        }
        catch (OperationCanceledException)
        {
            item.State = QueueState.Canceled;
            log.Status = "已取消";
        }
        catch (Exception ex)
        {
            item.State = QueueState.Failed;
            item.Note = ex.Message;
            log.Status = "失败";
            log.Error = ex.Message;
            LastError = ex.Message;
        }
        finally
        {
            item.FinishedAt = DateTime.Now;
            _player.Duck(false);
            StateChanged?.Invoke(item);

            if (!isPreview)
            {
                Logs.Insert(0, log);
                _store.Data.Logs.Add(log);
                while (Logs.Count > 200) Logs.RemoveAt(Logs.Count - 1);
                if (Queue.Contains(item) && item.State is QueueState.Done or QueueState.Failed or QueueState.Canceled)
                {
                    // 保留最近 30 条完成记录用于界面回看
                    var finished = Queue.Where(q => q.State is QueueState.Done or QueueState.Failed or QueueState.Canceled).ToList();
                    if (finished.Count > 30) Queue.Remove(finished.First());
                }
                _store.Save();
            }
            Changed?.Invoke();
        }
    }

    public void StartBgm()
    {
        var s = _store.Data.Store;
        if (!s.BgmEnabled || string.IsNullOrWhiteSpace(s.BgmPath)) return;
        _player.SetBgmVolume(s.BgmVolume);
        _player.StartBgm(s.BgmPath, s.BgmVolume);
    }

    public void StopBgm() => _player.StopBgm();

    public void ApplyVolume() => _player.MasterVolume = _store.Data.Store.MasterVolume;
}
