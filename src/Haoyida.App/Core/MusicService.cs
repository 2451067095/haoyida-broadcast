using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;

namespace Haoyida.Core;

/// <summary>
/// 背景音乐服务：按“音乐任务”的时段自动切换播放列表，支持顺序/随机、循环、切歌、闪避。
/// 与播报队列相互独立，人声播报时自动压低音量。
/// </summary>
public sealed class MusicService
{
    private readonly DataStore _store;
    private readonly AudioPlayer _player;
    private readonly DispatcherTimer _timer;
    private readonly Random _rng = new();

    private int _index;

    public ObservableCollection<MusicTrack> Queue { get; } = new();
    public MusicTask? ActiveTask { get; private set; }
    public MusicTrack? CurrentTrack { get; private set; }
    public bool IsPlaying => CurrentTrack is not null && _player.IsBgmPlaying;

    public event Action? Changed;

    public MusicService(DataStore store, AudioPlayer player)
    {
        _store = store;
        _player = player;
        _player.BgmLoop = false;
        _player.BgmEnded += () => Next(auto: true);

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Evaluate();
    }

    public void Start()
    {
        _timer.Start();
        Evaluate();
    }

    public void RefreshVolume()
    {
        _player.SetBgmVolume(_store.Data.Store.BgmVolume);
    }

    private void Evaluate()
    {
        var now = DateTime.Now;
        var active = _store.Data.MusicTasks.FirstOrDefault(t => t.Enabled && InWindow(t, now) && InWeekday(t, now));

        foreach (var t in _store.Data.MusicTasks) t.IsActive = ReferenceEquals(t, active);

        if (ReferenceEquals(active, ActiveTask)) return;

        if (active is null)
        {
            Stop();
            return;
        }

        PlayTask(active);
    }

    private static bool InWindow(MusicTask t, DateTime now)
    {
        if (!TimeSpan.TryParse(t.StartTime, out var start)) start = new TimeSpan(7, 30, 0);
        if (!TimeSpan.TryParse(t.EndTime, out var end)) end = new TimeSpan(21, 30, 0);
        var tod = now.TimeOfDay;
        return end >= start ? tod >= start && tod < end : tod >= start || tod < end;
    }

    private static bool InWeekday(MusicTask t, DateTime now)
    {
        if (t.Weekdays is null || t.Weekdays.Count == 0 || t.Weekdays.Count == 7) return true;
        int dow = ((int)now.DayOfWeek + 6) % 7 + 1; // 1=周一 … 7=周日
        return t.Weekdays.Contains(dow);
    }

    public void PlayTask(MusicTask task)
    {
        ActiveTask = task;
        Queue.Clear();
        foreach (var id in task.TrackIds)
        {
            var track = _store.Data.MusicTracks.FirstOrDefault(x => x.Id == id);
            if (track is not null && File.Exists(track.FullPath)) Queue.Add(track);
        }

        if (Queue.Count == 0)
        {
            Stop(keepTask: true);
            return;
        }

        _index = task.Shuffle ? _rng.Next(Queue.Count) : 0;
        PlayCurrent();
    }

    private void PlayCurrent()
    {
        if (ActiveTask is null || Queue.Count == 0) return;
        var track = Queue[Math.Clamp(_index, 0, Queue.Count - 1)];
        CurrentTrack = track;
        _player.SetBgmVolume(_store.Data.Store.BgmVolume);
        _player.StartBgm(track.FullPath, _store.Data.Store.BgmVolume);
        Changed?.Invoke();
    }

    public void Next(bool auto = false)
    {
        if (Queue.Count == 0) return;

        if (ActiveTask?.Shuffle == true && Queue.Count > 1)
        {
            int next;
            do { next = _rng.Next(Queue.Count); } while (next == _index);
            _index = next;
        }
        else
        {
            _index++;
            if (_index >= Queue.Count)
            {
                if (ActiveTask?.Loop == false && !auto) _index = 0;
                else _index = 0;
            }
        }

        PlayCurrent();
    }

    public void Stop(bool keepTask = false)
    {
        _player.StopBgm();
        CurrentTrack = null;
        if (!keepTask) ActiveTask = null;
        Changed?.Invoke();
    }

    public void Restart()
    {
        Stop();
        Evaluate();
    }
}
