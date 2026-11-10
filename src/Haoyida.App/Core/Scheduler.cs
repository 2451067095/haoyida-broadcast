using System.Windows.Threading;

namespace Haoyida.Core;

/// <summary>播出计划调度：每秒 tick，负责计算“下一次该播什么”。</summary>
public sealed class Scheduler
{
    private readonly DataStore _store;
    private readonly DispatcherTimer _timer;
    private bool _busy;

    public event Action<BroadcastTask>? TaskDue;

    public Scheduler(DataStore store)
    {
        _store = store;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Tick();
    }

    public void Start()
    {
        foreach (var t in _store.Data.Tasks) t.NextRun = ComputeNext(t, DateTime.Now);
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    public void RecalculateAll()
    {
        foreach (var t in _store.Data.Tasks) t.NextRun = ComputeNext(t, DateTime.Now);
    }

    private void Tick()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var now = DateTime.Now;
            foreach (var task in _store.Data.Tasks.ToList())
            {
                if (!task.Enabled) continue;
                if (task.NextRun is not { } next) { task.NextRun = ComputeNext(task, now); continue; }
                if (next > now) continue;

                task.LastRun = now;
                task.RunCount++;
                task.NextRun = ComputeNext(task, now.AddSeconds(1));
                TaskDue?.Invoke(task);
            }
        }
        finally
        {
            _busy = false;
        }
    }

    public static DateTime? ComputeNext(BroadcastTask t, DateTime now)
    {
        switch (t.Kind)
        {
            case ScheduleKind.Once:
            {
                var at = t.OnceAt ?? At(now.Date, t.Time);
                return at > now ? at : null;
            }
            case ScheduleKind.Daily:
            {
                var at = At(now.Date, t.Time);
                if (at <= now) at = at.AddDays(1);
                return InWindow(t, at) ? at : At(at.Date.AddDays(1), t.StartTime);
            }
            case ScheduleKind.Weekly:
            {
                for (int i = 0; i < 8; i++)
                {
                    var day = now.Date.AddDays(i);
                    if (!t.Weekdays.Contains(((int)day.DayOfWeek + 6) % 7 + 1)) continue;
                    var at = At(day, t.Time);
                    if (at > now && InWindow(t, at)) return at;
                }
                return At(now.Date.AddDays(1), t.Time);
            }
            case ScheduleKind.Interval:
            {
                var start = At(now.Date, t.StartTime);
                var end = At(now.Date, t.EndTime);
                if (end <= start) end = end.AddDays(1);

                if (now < start) return start;

                var anchor = t.LastRun is { } last && last.Date == now.Date ? last : start;
                var next = anchor.AddMinutes(Math.Max(1, t.IntervalMinutes));
                if (next < now) next = now.AddMinutes(Math.Max(1, t.IntervalMinutes));
                if (next > end) return At(now.Date.AddDays(1), t.StartTime);
                return next;
            }
            default:
                return null;
        }
    }

    private static bool InWindow(BroadcastTask t, DateTime at)
    {
        var start = TimeSpan.TryParse(t.StartTime, out var s) ? s : TimeSpan.Zero;
        var end = TimeSpan.TryParse(t.EndTime, out var e) ? e : new TimeSpan(23, 59, 59);
        var tod = at.TimeOfDay;
        return end >= start ? tod >= start && tod <= end : tod >= start || tod <= end;
    }

    private static DateTime At(DateTime day, string hm)
    {
        if (!TimeSpan.TryParse(hm, out var ts) || ts >= TimeSpan.FromDays(1)) ts = new TimeSpan(9, 0, 0);
        return day.Date + ts;
    }
}
