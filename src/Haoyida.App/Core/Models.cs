using System.IO;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Haoyida.Core;

/// <summary>轻量通知基类：整个工程不依赖任何 MVVM 框架。</summary>
public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    protected void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public enum QueueState { Queued, Synthesizing, Speaking, Done, Failed, Canceled }

public enum ScheduleKind { Daily, Weekly, Interval, Once }

public enum SourceKind { Script, Product, FreeText, Recording }

public sealed class StoreProfile : Observable
{
    private string _storeName = "耗易达生活超市";
    private string _branch = "旗舰店";
    private string _openTime = "07:30";
    private string _closeTime = "22:00";
    private string _phone = "";
    private double _masterVolume = 0.85;
    private string _bgmPath = "";
    private double _bgmVolume = 0.30;
    private double _duckLevel = 0.22;
    private bool _bgmEnabled = true;
    private bool _readPriceAsChinese = true;
    private bool _autoStart = false;
    private int _cacheDays = 30;
    private string _defaultEngineId = "piper";
    private string _defaultVoiceId = "huayan";
    private double _defaultRate = 1.0;
    private double _defaultPitch = 1.0;

    public string StoreName { get => _storeName; set => Set(ref _storeName, value); }
    public string Branch { get => _branch; set => Set(ref _branch, value); }
    public string OpenTime { get => _openTime; set => Set(ref _openTime, value); }
    public string CloseTime { get => _closeTime; set => Set(ref _closeTime, value); }
    public string Phone { get => _phone; set => Set(ref _phone, value); }
    public double MasterVolume { get => _masterVolume; set => Set(ref _masterVolume, value); }
    public string BgmPath { get => _bgmPath; set => Set(ref _bgmPath, value); }
    public double BgmVolume { get => _bgmVolume; set => Set(ref _bgmVolume, value); }
    public double DuckLevel { get => _duckLevel; set => Set(ref _duckLevel, value); }
    public bool BgmEnabled { get => _bgmEnabled; set => Set(ref _bgmEnabled, value); }
    public bool ReadPriceAsChinese { get => _readPriceAsChinese; set => Set(ref _readPriceAsChinese, value); }
    public bool AutoStart { get => _autoStart; set => Set(ref _autoStart, value); }
    public int CacheDays { get => _cacheDays; set => Set(ref _cacheDays, Math.Clamp(value, 1, 365)); }
    public string DefaultEngineId { get => _defaultEngineId; set => Set(ref _defaultEngineId, value); }
    public string DefaultVoiceId { get => _defaultVoiceId; set => Set(ref _defaultVoiceId, value); }
    public double DefaultRate { get => _defaultRate; set => Set(ref _defaultRate, value); }
    public double DefaultPitch { get => _defaultPitch; set => Set(ref _defaultPitch, value); }
}

public sealed class ScriptItem : Observable
{
    private string _title = "";
    private string _category = "促销";
    private string _body = "";
    private bool _enabled = true;
    private string _audioPath = "";
    private int _playCount;
    private DateTime? _lastPlayed;

    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Title { get => _title; set => Set(ref _title, value); }
    public string Category { get => _category; set => Set(ref _category, value); }
    public string Body { get => _body; set => Set(ref _body, value); }
    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
    public string AudioPath { get => _audioPath; set => Set(ref _audioPath, value); }
    public int PlayCount { get => _playCount; set => Set(ref _playCount, value); }
    public DateTime? LastPlayed { get => _lastPlayed; set => Set(ref _lastPlayed, value); }

    [JsonIgnore] public bool HasRecording => !string.IsNullOrWhiteSpace(AudioPath) && File.Exists(AudioPath);
    [JsonIgnore] public string BodyPreview => Body.Length <= 46 ? Body : Body[..46] + "…";
}

public sealed class ProductItem : Observable
{
    private string _name = "";
    private string _unit = "斤";
    private string _category = "生鲜";
    private string _promoText = "";
    private decimal? _originalPrice;
    private decimal? _promoPrice;
    private bool _enabled = true;

    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get => _name; set => Set(ref _name, value); }
    public string Unit { get => _unit; set => Set(ref _unit, value); }
    public string Category { get => _category; set => Set(ref _category, value); }
    public string PromoText { get => _promoText; set => Set(ref _promoText, value); }
    public decimal? OriginalPrice { get => _originalPrice; set { if (Set(ref _originalPrice, value)) Raise(nameof(PriceText)); } }
    public decimal? PromoPrice { get => _promoPrice; set { if (Set(ref _promoPrice, value)) { Raise(nameof(PriceText)); Raise(nameof(SaveText)); } } }
    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }

    [JsonIgnore] public string PriceText => PromoPrice is { } p ? $"￥{p:0.##}/{Unit}" : "—";
    [JsonIgnore] public string SaveText
    {
        get
        {
            if (OriginalPrice is { } o && PromoPrice is { } p && o > p) return $"省 {o - p:0.##}";
            return "";
        }
    }
}

public sealed class BroadcastTask : Observable
{
    private string _name = "";
    private ScheduleKind _kind = ScheduleKind.Daily;
    private string _time = "09:00";
    private int _intervalMinutes = 30;
    private string _startTime = "07:30";
    private string _endTime = "22:00";
    private bool _enabled = true;
    private int _priority = 10;
    private SourceKind _sourceKind = SourceKind.Script;
    private string _scriptId = "";
    private string _productId = "";
    private string _freeText = "";
    private string _engineId = "";
    private string _voiceId = "";
    private double _volume = 1.0;
    private DateTime? _nextRun;
    private DateTime? _lastRun;
    private int _runCount;

    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get => _name; set => Set(ref _name, value); }
    public ScheduleKind Kind { get => _kind; set { if (Set(ref _kind, value)) { Raise(nameof(KindText)); Raise(nameof(PatternText)); } } }
    public List<int> Weekdays { get; set; } = new() { 1, 2, 3, 4, 5, 6, 7 };
    public string Time { get => _time; set { if (Set(ref _time, value)) Raise(nameof(PatternText)); } }
    public DateTime? OnceAt { get; set; }
    public int IntervalMinutes { get => _intervalMinutes; set { if (Set(ref _intervalMinutes, value)) Raise(nameof(PatternText)); } }
    public string StartTime { get => _startTime; set => Set(ref _startTime, value); }
    public string EndTime { get => _endTime; set => Set(ref _endTime, value); }
    public bool Enabled { get => _enabled; set { if (Set(ref _enabled, value)) { Raise(nameof(StateText)); Raise(nameof(StateBrushKey)); } } }
    public int Priority { get => _priority; set => Set(ref _priority, value); }
    public SourceKind SourceKind { get => _sourceKind; set { if (Set(ref _sourceKind, value)) Raise(nameof(SourceText)); } }
    public string ScriptId { get => _scriptId; set => Set(ref _scriptId, value); }
    public string ProductId { get => _productId; set => Set(ref _productId, value); }
    public string FreeText { get => _freeText; set => Set(ref _freeText, value); }
    public string EngineId { get => _engineId; set => Set(ref _engineId, value); }
    public string VoiceId { get => _voiceId; set => Set(ref _voiceId, value); }
    public double Volume { get => _volume; set => Set(ref _volume, value); }
    public DateTime? NextRun { get => _nextRun; set { if (Set(ref _nextRun, value)) Raise(nameof(NextRunText)); } }
    public DateTime? LastRun { get => _lastRun; set { if (Set(ref _lastRun, value)) Raise(nameof(LastRunText)); } }
    public int RunCount { get => _runCount; set => Set(ref _runCount, value); }

    [JsonIgnore] public string KindText => Kind switch
    {
        ScheduleKind.Daily => "每天",
        ScheduleKind.Weekly => "每周",
        ScheduleKind.Interval => "循环",
        ScheduleKind.Once => "单次",
        _ => "—"
    };

    [JsonIgnore] public string PatternText => Kind switch
    {
        ScheduleKind.Daily => $"每天 {Time}",
        ScheduleKind.Weekly => $"每周{WeekdayText} {Time}",
        ScheduleKind.Interval => $"{StartTime}–{EndTime} 每 {IntervalMinutes} 分钟",
        ScheduleKind.Once => $"{Time} 单次",
        _ => "—"
    };

    private string WeekdayText
    {
        get
        {
            string[] n = { "一", "二", "三", "四", "五", "六", "日" };
            var days = Weekdays.OrderBy(d => d).Select(d => n[Math.Clamp(d - 1, 0, 6)]).ToArray();
            return days.Length == 7 ? "一至日" : string.Concat(days);
        }
    }

    [JsonIgnore] public string StateText => Enabled ? "运行中" : "已暂停";
    [JsonIgnore] public string StateBrushKey => Enabled ? "OkBrush" : "MutedBrush";
    [JsonIgnore] public string SourceText => SourceKind switch
    {
        SourceKind.Script => "文案库",
        SourceKind.Product => "促销商品",
        SourceKind.FreeText => "自由文本",
        SourceKind.Recording => "录音文件",
        _ => "—"
    };
    [JsonIgnore] public string NextRunText => NextRun is { } t
        ? (t.Date == DateTime.Today ? t.ToString("HH:mm") : $"明天 {t:HH:mm}")
        : "—";
    [JsonIgnore] public string LastRunText => LastRun is { } t ? t.ToString("MM-dd HH:mm") : "未执行";
}

public sealed class QueueItem : Observable
{
    private QueueState _state = QueueState.Queued;
    private string _note = "";

    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public int Priority { get; set; } = 10;
    public string EngineId { get; set; } = "";
    public string VoiceId { get; set; } = "";
    public double Rate { get; set; } = 1.0;
    public double Pitch { get; set; } = 1.0;
    public double Volume { get; set; } = 1.0;
    public string Origin { get; set; } = "计划";
    public DateTime EnqueuedAt { get; set; } = DateTime.Now;
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }

    public QueueState State { get => _state; set { if (Set(ref _state, value)) { Raise(nameof(StateText)); Raise(nameof(StateKey)); } } }
    public string Note { get => _note; set => Set(ref _note, value); }

    [JsonIgnore] public string StateText => State switch
    {
        QueueState.Queued => "排队中",
        QueueState.Synthesizing => "合成中",
        QueueState.Speaking => "播报中",
        QueueState.Done => "已完成",
        QueueState.Failed => "失败",
        QueueState.Canceled => "已取消",
        _ => "—"
    };

    [JsonIgnore] public string StateKey => State switch
    {
        QueueState.Queued => "MutedBrush",
        QueueState.Synthesizing => "WarnBrush",
        QueueState.Speaking => "AccentBrush",
        QueueState.Done => "OkBrush",
        _ => "DangerBrush"
    };

    [JsonIgnore] public string PriorityText => Priority <= 0 ? "插播" : Priority <= 10 ? "计划" : "轮播";
    [JsonIgnore] public string TimeText => EnqueuedAt.ToString("HH:mm:ss");
}

public sealed class LogEntry
{
    public DateTime At { get; set; } = DateTime.Now;
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public string Status { get; set; } = "成功";
    public string Engine { get; set; } = "";
    public string Voice { get; set; } = "";
    public long ElapsedMs { get; set; }
    public string Error { get; set; } = "";

    public string TimeText => At.ToString("MM-dd HH:mm:ss");
    public string StatusKey => Status switch { "成功" => "OkBrush", "已取消" => "MutedBrush", _ => "DangerBrush" };
    public string DurationText => $"{ElapsedMs} ms";
}

public sealed class MusicTrack : Observable
{
    private string _title = "";
    private string _filePath = "";

    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Title { get => _title; set => Set(ref _title, value); }
    /// <summary>内置曲目为相对文件名（位于 Assets\music），用户曲目为绝对路径。</summary>
    public string FilePath { get => _filePath; set => Set(ref _filePath, value); }
    public bool IsBuiltIn { get; set; }
    public string Mood { get; set; } = "";

    [JsonIgnore]
    public string FullPath => IsBuiltIn
        ? Path.Combine(AppContext.BaseDirectory, "Assets", "music", FilePath)
        : FilePath;

    [JsonIgnore] public string KindText => IsBuiltIn ? "内置" : "本地";
}

public sealed class MusicTask : Observable
{
    private string _name = "";
    private string _startTime = "07:30";
    private string _endTime = "21:30";
    private bool _loop = true;
    private bool _shuffle = false;
    private bool _enabled = true;
    private bool _isActive;

    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get => _name; set => Set(ref _name, value); }
    public string StartTime { get => _startTime; set { if (Set(ref _startTime, value)) Raise(nameof(WindowText)); } }
    public string EndTime { get => _endTime; set { if (Set(ref _endTime, value)) Raise(nameof(WindowText)); } }
    public bool Loop { get => _loop; set => Set(ref _loop, value); }
    public bool Shuffle { get => _shuffle; set { if (Set(ref _shuffle, value)) Raise(nameof(ModeText)); } }
    public bool Enabled { get => _enabled; set { if (Set(ref _enabled, value)) Raise(nameof(StateText)); } }
    public List<int> Weekdays { get; set; } = new() { 1, 2, 3, 4, 5, 6, 7 };
    public List<string> TrackIds { get; set; } = new();

    [JsonIgnore] public bool IsActive { get => _isActive; set { if (Set(ref _isActive, value)) Raise(nameof(StateText)); } }
    [JsonIgnore] public string WindowText => $"{StartTime} – {EndTime}";
    [JsonIgnore] public string ModeText => Shuffle ? "随机" : "顺序";
    [JsonIgnore] public string StateText => !Enabled ? "已停用" : IsActive ? "播放中" : "待命";
    [JsonIgnore] public string StateBrushKey => !Enabled ? "MutedBrush" : IsActive ? "OkBrush" : "AccentBrush";
    [JsonIgnore] public int TrackCount => TrackIds.Count;

    public void RefreshTrackCount() => Raise(nameof(TrackCount));
}
public sealed class AppData
{
    public int SchemaVersion { get; set; } = 1;
    public StoreProfile Store { get; set; } = new();
    public List<ScriptItem> Scripts { get; set; } = new();
    public List<ProductItem> Products { get; set; } = new();
    public List<BroadcastTask> Tasks { get; set; } = new();
    public List<LogEntry> Logs { get; set; } = new();
    public List<MusicTrack> MusicTracks { get; set; } = new();
    public List<MusicTask> MusicTasks { get; set; } = new();
    public List<CustomTtsModel> CustomModels { get; set; } = new();
}
