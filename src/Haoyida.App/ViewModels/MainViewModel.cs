using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using Haoyida.Core;
using Haoyida.Tts;

namespace Haoyida.ViewModels;

public sealed class EngineCard : Observable
{
    private bool _ready;
    private string _status = "检测中…";

    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Vendor { get; init; }
    public required string License { get; init; }
    public required string Kind { get; init; }
    public required string Note { get; init; }
    public required string Glyph { get; init; }

    public bool Ready { get => _ready; set { if (Set(ref _ready, value)) { Raise(nameof(StatusBrushKey)); } } }
    public string Status { get => _status; set => Set(ref _status, value); }
    public int VoiceCount { get; set; }
    public string StatusBrushKey => Ready ? "OkBrush" : "DangerBrush";
    public string VoiceCountText => VoiceCount > 0 ? $"{VoiceCount} 个音色" : "不可用";
}

public sealed class UpcomingRow
{
    public string Time { get; init; } = "";
    public string Name { get; init; } = "";
    public string Kind { get; init; } = "";
    public string Countdown { get; set; } = "";
}

public sealed class HourBar
{
    public string Label { get; init; } = "";
    public int Count { get; set; }
    public double Height { get; set; }
}

public sealed partial class MainViewModel : Observable
{
    private readonly DataStore _store;
    private readonly AudioPlayer _player;
    private readonly DispatcherTimer _uiTimer;

    private int _pageIndex;
    private string _quickText = "";
    private bool _isRunning = true;
    private string _clockText = "";
    private string _countdownText = "—";
    private string _searchText = "";
    private string _selectedCategory = "全部";
    private string _logFilter = "全部";
    private VoiceOption? _selectedVoice;
    private EngineCard? _selectedEngine;
    private double _rate = 1.0;
    private double _pitch = 1.0;
    private string _styleId = "warm";
    private string _previewText = "欢迎光临，本店今日鲜果特惠，红富士苹果每斤四块九毛八。";
    private bool _isPreviewing;
    private string _cacheText = "";
    private BroadcastTask? _selectedTask;
    private ScriptItem? _selectedScript;
    private ProductItem? _selectedProduct;

    public DataStore Store { get; }
    public IServiceProvider Services => throw new NotSupportedException();
    public TtsService Tts { get; }
    public AudioPlayer Player { get; }
    public BroadcastEngine Engine { get; }
    public Scheduler Scheduler { get; }

    public MainViewModel(DataStore store, TtsService tts, AudioPlayer player, BroadcastEngine engine, Scheduler scheduler, MusicService music)
    {
        Store = store;
        Tts = tts;
        Player = player;
        Engine = engine;
        Scheduler = scheduler;

        _store = store;
        _player = player;
        InitMusic(music);
        InitCustomModels();
        InitVoicePickers();
        InitProductExcel();

        Tasks = new ObservableCollection<BroadcastTask>(store.Data.Tasks);
        Scripts = new ObservableCollection<ScriptItem>(store.Data.Scripts);
        Products = new ObservableCollection<ProductItem>(store.Data.Products);

        ScriptView = CollectionViewSource.GetDefaultView(Scripts);
        ScriptView.Filter = FilterScript;
        ProductView = CollectionViewSource.GetDefaultView(Products);
        LogView = CollectionViewSource.GetDefaultView(Engine.Logs);
        LogView.Filter = FilterLog;

        EngineCards = new ObservableCollection<EngineCard>();
        Voices = new ObservableCollection<VoiceOption>();
        Upcoming = new ObservableCollection<UpcomingRow>();
        HourBars = new ObservableCollection<HourBar>();
        Categories = new ObservableCollection<string> { "全部" };

        NewTaskCommand = new RelayCommand(_ => NewTask());
        DeleteTaskCommand = new RelayCommand(_ => DeleteTask(), _ => SelectedTask is not null);
        RunTaskCommand = new RelayCommand(_ => RunTaskNow(), _ => SelectedTask is not null);
        NewScriptCommand = new RelayCommand(_ => NewScript());
        DeleteScriptCommand = new RelayCommand(_ => DeleteScript(), _ => SelectedScript is not null);
        PreviewScriptCommand = new RelayCommand(_ => _ = PreviewScriptAsync(), _ => SelectedScript is not null);
        InsertTokenCommand = new RelayCommand(p => InsertToken(p as string ?? ""));
        NewProductCommand = new RelayCommand(_ => NewProduct());
        DeleteProductCommand = new RelayCommand(_ => DeleteProduct(), _ => SelectedProduct is not null);
        GenerateProductCommand = new RelayCommand(_ => GenerateProductText());
        PreviewProductCommand = new RelayCommand(_ => _ = PreviewProductAsync(), _ => SelectedProduct is not null);
        PreviewVoiceCommand = new RelayCommand(_ => _ = PreviewVoiceAsync());
        ProbeEdgeCommand = new RelayCommand(_ => _ = ProbeEdgeAsync());
        ClearCacheCommand = new RelayCommand(_ => ClearCache());
        CleanOldCacheCommand = new RelayCommand(_ => CleanOldCache());
        ChooseBgmCommand = new RelayCommand(_ => ChooseBgm());
        ClearBgmCommand = new RelayCommand(_ => { Profile.BgmPath = ""; Engine.StopBgm(); Save(); });
        OpenDataFolderCommand = new RelayCommand(_ => OpenDataFolder());
        ExportLogsCommand = new RelayCommand(_ => ExportLogs());
        ClearLogsCommand = new RelayCommand(_ => { Engine.Logs.Clear(); _store.Data.Logs.Clear(); Save(); });
        SetLogFilterCommand = new RelayCommand(p => { LogFilter = p as string ?? "全部"; });
        SaveCommand = new RelayCommand(_ => Save());
        SpeakQuickCommand = new RelayCommand(_ => SpeakQuick());
        InterruptCommand = new RelayCommand(_ => Interrupt());
        ApplyStyleCommand = new RelayCommand(p => ApplyStyle(p as string));
        CancelQueueCommand = new RelayCommand(p => { if (p is QueueItem q) Engine.Cancel(q); });
        ClearQueueCommand = new RelayCommand(_ => { Engine.ClearQueue(); Raise(nameof(QueueCount)); });
        StartStopCommand = new RelayCommand(_ => ToggleRun());
        RepeatTaskCommand = new RelayCommand(p =>
        {
            if (p is UpcomingRow) return;
        });

        _uiTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _uiTimer.Tick += (_, _) => OnTick();

        Scheduler.TaskDue += OnTaskDue;
        Engine.Changed += () =>
        {
            Raise(nameof(QueueCount));
            Raise(nameof(IsSpeaking));
            Raise(nameof(CurrentTitle));
            Raise(nameof(CurrentText));
            Raise(nameof(CurrentStateText));
            Raise(nameof(TodayCount));
            Raise(nameof(LastBroadcastText));
        };

        BuildEngineCards();
        RefreshCategories();
        RebuildHourBars();
        RefreshUpcoming();
        UpdateCacheText();
    }

    public void Start()
    {
        Scheduler.Start();
        _uiTimer.Start();
        OnTick();
        Tts.ClearCacheOlderThan(Profile.CacheDays);
        Music.Start();
        SelectDefaultVoice();
        UpdateEngineStatus();

        SelectedTask = Tasks.FirstOrDefault();
        SelectedScript = Scripts.FirstOrDefault();
        SelectedProduct = Products.FirstOrDefault();
    }

    // ================= 导航 / 页头 =================
    public int PageIndex
    {
        get => _pageIndex;
        set
        {
            if (value < 0 || value > 7) return;
            if (!Set(ref _pageIndex, value)) return;
            Raise(nameof(PageTitle));
            Raise(nameof(PageSubtitle));
            Raise(nameof(PageSlug));
        }
    }

    public string PageSlug => PageIndex switch
    {
        0 => "dashboard",
        1 => "plan",
        2 => "scripts",
        3 => "products",
        4 => "music",
        5 => "voices",
        6 => "logs",
        _ => "settings"
    };

    public string PageTitle => PageIndex switch
    {
        0 => "播报台",
        1 => "播报计划",
        2 => "文案库",
        3 => "促销商品",
        4 => "背景音乐",
        5 => "音色引擎",
        6 => "播报日志",
        _ => "系统设置"
    };

    public string PageSubtitle => PageIndex switch
    {
        0 => "门店实时播报状态、队列与今日节奏",
        1 => "按天、按周或按时段循环执行播报任务",
        2 => "可复用的播报词，支持变量与在线试听",
        3 => "维护商品与促销价，自动生成口语化播报词",
        4 => "按时段自动播放的音乐任务，支持本地音频",
        5 => "多引擎、多音色、多风格，一处切换",
        6 => "每一次播报都有据可查",
        _ => "门店信息、音频与播报偏好"
    };

    public string ClockText { get => _clockText; private set => Set(ref _clockText, value); }
    public string CountdownText { get => _countdownText; private set => Set(ref _countdownText, value); }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (!Set(ref _isRunning, value)) return;
            Raise(nameof(RunStateText));
            Raise(nameof(RunStateKey));
            Raise(nameof(RunStateDetail));
        }
    }

    public string RunStateText => IsRunning ? "正在值守" : "已暂停";
    public string RunStateKey => IsRunning ? "OkBrush" : "WarnBrush";
    public string RunStateDetail => IsRunning ? "计划任务按时间自动播报" : "计划任务已停止，插播仍然可用";

    public int TodayCount => Engine.TodayCount;
    public int QueueCount => Engine.Queue.Count;
    public int EnabledTaskCount => Tasks.Count(t => t.Enabled);
    public int ProductCount => Products.Count;
    public int ScriptCount => Scripts.Count;

    public string LastBroadcastText
    {
        get
        {
            var last = Engine.LastSuccess;
            return last is null ? "还没有播报记录" : $"{last.At:HH:mm} · {last.Title}";
        }
    }

    public bool IsSpeaking => Engine.IsSpeaking;
    public string CurrentTitle => Engine.Current?.Title ?? (IsRunning ? "待机中" : "已暂停");
    public string CurrentText => Engine.Current?.Text ?? "等待下一条播报…";
    public string CurrentStateText => Engine.Current?.StateText ?? "空闲";

    // ================= 快捷插播 =================
    public string QuickText { get => _quickText; set => Set(ref _quickText, value); }

    public RelayCommand SpeakQuickCommand { get; }
    public RelayCommand InterruptCommand { get; }
    public RelayCommand StartStopCommand { get; }

    private void SpeakQuick()
    {
        if (string.IsNullOrWhiteSpace(QuickText)) return;
        var text = TemplateEngine.Render(QuickText, Profile);
        Engine.Enqueue(Engine.MakeItem("手动播报", text, 0, "手动"));
        QuickText = "";
    }

    private void Interrupt()
    {
        var text = string.IsNullOrWhiteSpace(QuickText)
            ? $"{Profile.StoreName}提醒您，请注意随身物品，照顾好老人和小孩。"
            : TemplateEngine.Render(QuickText, Profile);
        Engine.Interrupt("紧急插播", text);
        QuickText = "";
    }

    private void ToggleRun()
    {
        IsRunning = !IsRunning;
        if (IsRunning) Scheduler.Start(); else Scheduler.Stop();
    }

    // ================= 计划 =================
    public ObservableCollection<BroadcastTask> Tasks { get; }
    public BroadcastTask? SelectedTask
    {
        get => _selectedTask;
        set
        {
            if (!Set(ref _selectedTask, value)) return;
            Raise(nameof(HasSelectedTask));
            Raise(nameof(TaskSourceLabel));
            Raise(nameof(TaskKindIndex));
            Raise(nameof(TaskSourceIndex));
            Raise(nameof(WeeklyDayIndex));
            SyncTaskVoice();
        }
    }

    public bool HasSelectedTask => SelectedTask is not null;
    public string TaskSourceLabel => SelectedTask?.SourceText ?? "";

    public int TaskKindIndex
    {
        get => SelectedTask is null ? 0 : (int)SelectedTask.Kind;
        set
        {
            if (SelectedTask is null) return;
            SelectedTask.Kind = (ScheduleKind)Math.Clamp(value, 0, 3);
            Raise(nameof(TaskKindIndex));
            if (IsRunning) SelectedTask.NextRun = Scheduler.ComputeNext(SelectedTask, DateTime.Now);
            Save();
        }
    }

    public int TaskSourceIndex
    {
        get => SelectedTask is null ? 0 : (int)SelectedTask.SourceKind;
        set
        {
            if (SelectedTask is null) return;
            SelectedTask.SourceKind = (SourceKind)Math.Clamp(value, 0, 3);
            Raise(nameof(TaskSourceIndex));
            Save();
        }
    }

    /// <summary>0=每天，1..7=周一..周日。写入 Weekdays。</summary>
    public int WeeklyDayIndex
    {
        get
        {
            if (SelectedTask is null || SelectedTask.Weekdays.Count != 1) return 0;
            return SelectedTask.Weekdays[0];
        }
        set
        {
            if (SelectedTask is null) return;
            SelectedTask.Weekdays = value <= 0
                ? new List<int> { 1, 2, 3, 4, 5, 6, 7 }
                : new List<int> { value };
            Raise(nameof(WeeklyDayIndex));
            Save();
        }
    }

    public RelayCommand NewTaskCommand { get; }
    public RelayCommand DeleteTaskCommand { get; }
    public RelayCommand RunTaskCommand { get; }

    private void NewTask()
    {
        var task = new BroadcastTask
        {
            Name = "新计划" + (Tasks.Count + 1),
            Kind = ScheduleKind.Daily,
            Time = DateTime.Now.AddMinutes(5).ToString("HH:mm"),
            ScriptId = Scripts.FirstOrDefault()?.Id ?? "",
            Priority = 10
        };
        Tasks.Add(task);
        _store.Data.Tasks.Add(task);
        SelectedTask = task;
        if (IsRunning) task.NextRun = Scheduler.ComputeNext(task, DateTime.Now);
        Save();
        RefreshUpcoming();
    }

    private void DeleteTask()
    {
        if (SelectedTask is not { } task) return;
        Tasks.Remove(task);
        _store.Data.Tasks.Remove(task);
        SelectedTask = null;
        Save();
        RefreshUpcoming();
    }

    private void RunTaskNow()
    {
        if (SelectedTask is not { } task) return;
        var text = ComposeTaskText(task);
        if (string.IsNullOrWhiteSpace(text)) return;
        Engine.Enqueue(Engine.FromTask(task, text));
        task.LastRun = DateTime.Now;
    }

    public RelayCommand ClearQueueCommand { get; } = null!;

    private void OnTaskDue(BroadcastTask task)
    {
        if (!IsRunning) return;
        var text = ComposeTaskText(task);
        if (string.IsNullOrWhiteSpace(text)) return;
        Engine.Enqueue(Engine.FromTask(task, text));
        RefreshUpcoming();
    }

    private string ComposeTaskText(BroadcastTask task)
    {
        switch (task.SourceKind)
        {
            case SourceKind.Script:
            {
                var script = Scripts.FirstOrDefault(s => s.Id == task.ScriptId);
                return script is null ? "" : TemplateEngine.Render(script.Body, Profile);
            }
            case SourceKind.Product:
            {
                var product = Products.FirstOrDefault(p => p.Id == task.ProductId);
                return product is null ? "" : TemplateEngine.DefaultProductLine(product, Profile);
            }
            case SourceKind.FreeText:
                return TemplateEngine.Render(task.FreeText, Profile);
            default:
                return "";
        }
    }

    // ================= 文案 =================
    public ObservableCollection<ScriptItem> Scripts { get; }
    public ICollectionView ScriptView { get; }
    public ObservableCollection<string> Categories { get; }
    public string SelectedCategory
    {
        get => _selectedCategory;
        set { if (Set(ref _selectedCategory, value)) { ScriptView.Refresh(); } }
    }

    public string SearchText
    {
        get => _searchText;
        set { if (Set(ref _searchText, value)) ScriptView.Refresh(); }
    }

    public ScriptItem? SelectedScript
    {
        get => _selectedScript;
        set
        {
            if (_selectedScript is not null) _selectedScript.PropertyChanged -= OnSelectedScriptEdited;
            if (!Set(ref _selectedScript, value)) return;
            if (_selectedScript is not null) _selectedScript.PropertyChanged += OnSelectedScriptEdited;
            Raise(nameof(HasSelectedScript));
            Raise(nameof(ScriptPreview));
        }
    }

    private void OnSelectedScriptEdited(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ScriptItem.Body) or nameof(ScriptItem.Title) or nameof(ScriptItem.Category))
            Raise(nameof(ScriptPreview));
    }

    /// <summary>渲染后的实际播报词（变量已替换），用于编辑器的“效果预览”。</summary>
    public string ScriptPreview => SelectedScript is { } s
        ? TemplateEngine.Render(s.Body, Profile, Products.FirstOrDefault())
        : "";

    public bool HasSelectedScript => SelectedScript is not null;

    public RelayCommand NewScriptCommand { get; }
    public RelayCommand DeleteScriptCommand { get; }
    public RelayCommand PreviewScriptCommand { get; }
    public RelayCommand InsertTokenCommand { get; }

    private bool FilterScript(object obj)
    {
        if (obj is not ScriptItem s) return false;
        if (SelectedCategory != "全部" && s.Category != SelectedCategory) return false;
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        return s.Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
               || s.Body.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshCategories()
    {
        string[] preferred = { "迎宾", "促销", "服务", "安全", "闭店", "节日" };
        var cats = Scripts.Select(s => s.Category).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct()
            .OrderBy(c => Array.IndexOf(preferred, c) is var i and >= 0 ? i : 99)
            .ThenBy(c => c).ToList();
        Categories.Clear();
        Categories.Add("全部");
        foreach (var c in cats) Categories.Add(c);
        if (!Categories.Contains(SelectedCategory)) SelectedCategory = "全部";
    }

    private void NewScript()
    {
        var script = new ScriptItem { Title = "新文案", Category = "促销", Body = "顾客朋友们请注意，{商品}正在特惠，现价{现价}元。" };
        Scripts.Add(script);
        _store.Data.Scripts.Add(script);
        SelectedScript = script;
        RefreshCategories();
        Save();
    }

    private void DeleteScript()
    {
        if (SelectedScript is not { } script) return;
        Scripts.Remove(script);
        _store.Data.Scripts.Remove(script);
        SelectedScript = null;
        RefreshCategories();
        Save();
    }

    private void InsertToken(string token)
    {
        if (SelectedScript is not { } script || string.IsNullOrEmpty(token)) return;
        script.Body += token;
    }

    private async Task PreviewScriptAsync()
    {
        if (SelectedScript is not { } script) return;
        var text = TemplateEngine.Render(script.Body, Profile, Products.FirstOrDefault());
        await PreviewAsync(text);
    }

    // ================= 商品 =================
    public ObservableCollection<ProductItem> Products { get; }
    public ICollectionView ProductView { get; }

    public ProductItem? SelectedProduct
    {
        get => _selectedProduct;
        set
        {
            if (_selectedProduct is not null) _selectedProduct.PropertyChanged -= OnSelectedProductEdited;
            if (!Set(ref _selectedProduct, value)) return;
            if (_selectedProduct is not null) _selectedProduct.PropertyChanged += OnSelectedProductEdited;
            Raise(nameof(HasSelectedProduct));
            Raise(nameof(ProductLine));
        }
    }

    private void OnSelectedProductEdited(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProductItem.Name) or nameof(ProductItem.Unit) or nameof(ProductItem.PromoPrice)
            or nameof(ProductItem.OriginalPrice) or nameof(ProductItem.PromoText))
            Raise(nameof(ProductLine));
    }

    public bool HasSelectedProduct => SelectedProduct is not null;

    public string ProductLine => SelectedProduct is { } p ? TemplateEngine.DefaultProductLine(p, Profile) : "";

    public RelayCommand NewProductCommand { get; }
    public RelayCommand DeleteProductCommand { get; }
    public RelayCommand GenerateProductCommand { get; }
    public RelayCommand PreviewProductCommand { get; }

    private void NewProduct()
    {
        var product = new ProductItem { Name = "新商品", Unit = "斤", OriginalPrice = 10, PromoPrice = 8.8m };
        Products.Add(product);
        _store.Data.Products.Add(product);
        SelectedProduct = product;
        Save();
    }

    private void DeleteProduct()
    {
        if (SelectedProduct is not { } product) return;
        Products.Remove(product);
        _store.Data.Products.Remove(product);
        SelectedProduct = null;
        Save();
    }

    private void GenerateProductText()
    {
        if (SelectedProduct is not { } product) return;
        var line = TemplateEngine.DefaultProductLine(product, Profile);
        var script = new ScriptItem { Title = product.Name + " 播报词", Category = "促销", Body = line };
        Scripts.Add(script);
        _store.Data.Scripts.Add(script);
        SelectedScript = script;
        PageIndex = 2;
        RefreshCategories();
        Save();
    }

    private async Task PreviewProductAsync()
    {
        if (SelectedProduct is not { } product) return;
        await PreviewAsync(TemplateEngine.DefaultProductLine(product, Profile));
    }

    // ================= 音色 =================
    public ObservableCollection<EngineCard> EngineCards { get; }
    public ObservableCollection<VoiceOption> Voices { get; }

    public EngineCard? SelectedEngine
    {
        get => _selectedEngine;
        set
        {
            if (!Set(ref _selectedEngine, value) || value is null) return;
            LoadVoicesFor(value.Id);
        }
    }

    public VoiceOption? SelectedVoice
    {
        get => _selectedVoice;
        set
        {
            if (!Set(ref _selectedVoice, value) || value is null) return;
            Profile.DefaultEngineId = value.EngineId;
            Profile.DefaultVoiceId = value.Id;
            Save();
        }
    }

    public ObservableCollection<VoiceStyles.Preset> StyleOptions { get; } = new(VoiceStyles.All);

    public string StyleId
    {
        get => _styleId;
        set { if (Set(ref _styleId, value)) ApplyStyle(value); }
    }

    public double Rate
    {
        get => _rate;
        set { if (Set(ref _rate, Math.Round(value, 2))) { Profile.DefaultRate = _rate; Save(); } }
    }

    public double Pitch
    {
        get => _pitch;
        set { if (Set(ref _pitch, Math.Round(value, 2))) { Profile.DefaultPitch = _pitch; Save(); } }
    }

    public string PreviewText { get => _previewText; set => Set(ref _previewText, value); }

    public bool IsPreviewing
    {
        get => _isPreviewing;
        private set { if (Set(ref _isPreviewing, value)) Raise(nameof(PreviewButtonText)); }
    }

    public string PreviewButtonText => IsPreviewing ? "停止试听" : "试听这段";

    private string _voiceDiag = "尚未试听。点击「试听这段」会在这里显示合成结果或错误原因。";
    public string VoiceDiagText { get => _voiceDiag; private set => Set(ref _voiceDiag, value); }

    public RelayCommand PreviewVoiceCommand { get; }
    public RelayCommand ProbeEdgeCommand { get; }
    public RelayCommand ClearCacheCommand { get; }
    public RelayCommand CleanOldCacheCommand { get; }
    public RelayCommand ApplyStyleCommand { get; }

    private void BuildEngineCards()
    {
        EngineCards.Clear();
        string[] glyphs = { "◈", "◉", "▣", "♪" };
        int i = 0;
        foreach (var engine in Tts.Engines)
        {
            var ready = engine.IsAvailable(out var reason);
            EngineCards.Add(new EngineCard
            {
                Id = engine.Id,
                Name = engine.Name,
                Vendor = engine.Vendor,
                License = engine.License,
                Kind = engine.Kind,
                Note = engine.Note,
                Glyph = glyphs[Math.Min(i++, glyphs.Length - 1)],
                Ready = ready,
                Status = ready
                    ? (engine.Id == "edge" ? "已安装 · 需联网" : "可用")
                    : reason,
                VoiceCount = ready ? engine.Voices.Count : 0
            });
        }
    }

    private void UpdateEngineStatus()
    {
        foreach (var card in EngineCards)
        {
            var engine = Tts.Engines.FirstOrDefault(e => e.Id == card.Id);
            if (engine is null) continue;
            var ready = engine.IsAvailable(out var reason);
            card.Ready = ready;
            card.Status = ready ? (engine.Id == "edge" ? "已安装 · 需联网" : "可用") : reason;
            card.VoiceCount = ready ? engine.Voices.Count : 0;
        }
    }

    private void LoadVoicesFor(string engineId)
    {
        Voices.Clear();
        var engine = Tts.Engines.FirstOrDefault(e => e.Id == engineId);
        if (engine is null || !engine.IsAvailable(out _)) return;
        foreach (var v in engine.Voices) Voices.Add(v);

        SelectedVoice = Voices.FirstOrDefault(v => v.Id == Profile.DefaultVoiceId) ?? Voices.FirstOrDefault();
    }

    private void SelectDefaultVoice()
    {
        // 旧配置可能把默认音色存成了英文 SAPI（Zira）：读中文会近乎无声，这里纠正为中文引擎。
        var savedEngine = Tts.Engines.FirstOrDefault(e => e.Id == Profile.DefaultEngineId);
        var savedVoice = savedEngine?.Voices.FirstOrDefault(v => v.Id == Profile.DefaultVoiceId);
        bool savedIsEnglishSapi = savedEngine?.Id == "sapi" &&
                                  (savedVoice?.Description.Contains("English", StringComparison.OrdinalIgnoreCase) ?? false);
        if (savedIsEnglishSapi)
        {
            var zh = Tts.Engines.FirstOrDefault(e => e.Id == "kokoro" && e.IsAvailable(out _))
                     ?? Tts.Engines.FirstOrDefault(e => e.Id == "piper" && e.IsAvailable(out _));
            var zhVoice = zh?.Voices.FirstOrDefault();
            if (zh is not null && zhVoice is not null)
            {
                Profile.DefaultEngineId = zh.Id;
                Profile.DefaultVoiceId = zhVoice.Id;
                Save();
            }
        }

        var card = EngineCards.FirstOrDefault(c => c.Id == Profile.DefaultEngineId && c.Ready)
                   ?? EngineCards.FirstOrDefault(c => c.Ready)
                   ?? EngineCards.FirstOrDefault();
        if (card is null) return;
        SelectedEngine = card;
    }

    private void ApplyStyle(string? id)
    {
        var preset = VoiceStyles.ById(id);
        _styleId = preset.Id;
        Raise(nameof(StyleId));
        Rate = preset.Rate;
        Pitch = preset.Pitch;
    }

    private async Task PreviewVoiceAsync()
    {
        if (IsPreviewing) { Player.StopVoice(); IsPreviewing = false; return; }
        IsPreviewing = true;
        try { await PreviewAsync(PreviewText); }
        finally { IsPreviewing = false; }
    }

    private async Task ProbeEdgeAsync()
    {
        var edge = Tts.Engines.OfType<EdgeTtsEngine>().FirstOrDefault();
        if (edge is null) return;
        var card = EngineCards.FirstOrDefault(c => c.Id == "edge");
        if (card is not null) card.Status = "正在联网自检…";

        var (ok, message) = await edge.ProbeServiceAsync(CancellationToken.None);
        if (card is not null)
        {
            card.Ready = ok;
            card.Status = ok ? "在线可用" : message.Length > 40 ? message[..40] : message;
        }
        UpdateEngineStatus();
    }

    /// <summary>命令行自检用：跑一次试听并返回诊断文本。</summary>
    public async Task<string> DiagnosePreviewAsync(string? text = null)
    {
        await PreviewAsync(text ?? PreviewText);
        return VoiceDiagText;
    }

    private async Task PreviewAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) { VoiceDiagText = "试听文本为空。"; return; }
        var engineId = SelectedVoice?.EngineId ?? Profile.DefaultEngineId;
        var voiceId = SelectedVoice?.Id ?? Profile.DefaultVoiceId;
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await Engine.PreviewAsync(text, engineId, voiceId, Rate, Pitch);
            sw.Stop();
            VoiceDiagText = string.IsNullOrEmpty(Engine.LastError)
                ? $"已播放 · 引擎 {engineId} · 音色 {voiceId} · 用时 {sw.ElapsedMilliseconds} ms"
                : "失败：" + Engine.LastError;
        }
        catch (Exception ex)
        {
            VoiceDiagText = "合成失败：" + ex.Message;
        }
    }

    // ================= 日志 =================
    public ICollectionView LogView { get; }
    public string LogFilter
    {
        get => _logFilter;
        set { if (Set(ref _logFilter, value)) LogView.Refresh(); }
    }

    public RelayCommand ExportLogsCommand { get; }
    public RelayCommand ClearLogsCommand { get; }
    public RelayCommand SetLogFilterCommand { get; }

    private bool FilterLog(object obj)
    {
        if (obj is not LogEntry e) return false;
        return LogFilter switch
        {
            "成功" => e.Status == "成功",
            "失败" => e.Status == "失败",
            "试听" => e.Status == "试听",
            "全部" => true,
            _ => true
        };
    }

    private void ExportLogs()
    {
        try
        {
            var file = Path.Combine(_store.ExportDirectory, $"广播日志-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
            var lines = new List<string> { "时间,标题,内容,状态,引擎,音色,耗时(ms),错误" };
            foreach (var e in Engine.Logs)
                lines.Add(string.Join(',', Csv(e.TimeText), Csv(e.Title), Csv(e.Text), Csv(e.Status), Csv(e.Engine), Csv(e.Voice), e.ElapsedMs, Csv(e.Error)));
            File.WriteAllLines(file, lines, System.Text.Encoding.UTF8);
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show("导出失败：" + ex.Message);
        }
    }

    private static string Csv(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";

    // ================= 设置 =================
    public StoreProfile Profile => _store.Data.Store;

    /// <summary>开机自启：写注册表并持久化。</summary>
    public bool AutoStartEnabled
    {
        get => Profile.AutoStart;
        set
        {
            if (Profile.AutoStart == value) return;
            Profile.AutoStart = value;
            Core.AutoStart.Apply(value, out var message);
            Raise();
            Raise(nameof(AutoStartHint));
            Save();
        }
    }

    public string AutoStartHint => Profile.AutoStart ? "已加入开机启动（当前用户）" : "开机后需要手动打开软件";
    public RelayCommand ChooseBgmCommand { get; }
    public RelayCommand ClearBgmCommand { get; }
    public RelayCommand OpenDataFolderCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand CancelQueueCommand { get; }
    public RelayCommand RepeatTaskCommand { get; }
    public string CacheText { get => _cacheText; private set => Set(ref _cacheText, value); }
    public string DataFolder => _store.DataDirectory;

    private void ChooseBgm()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择背景音乐",
            Filter = "音频文件|*.mp3;*.wav;*.wma;*.m4a|所有文件|*.*"
        };
        if (dialog.ShowDialog() != true) return;
        Profile.BgmPath = dialog.FileName;
        Profile.BgmEnabled = true;
        Tts.ClearCacheOlderThan(Profile.CacheDays);
        Music.Start();
        Save();
        Raise(nameof(Profile));
    }

    private void OpenDataFolder()
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", _store.DataDirectory) { UseShellExecute = true }); }
        catch { }
    }

    private void CleanOldCache()
    {
        var removed = Tts.ClearCacheOlderThan(Profile.CacheDays);
        UpdateCacheText();
        MessageBox.Show($"已清理 {removed} 个 {Profile.CacheDays} 天未使用的缓存文件。", "耗易达");
    }

    private void ClearCache()
    {
        Tts.ClearCache();
        UpdateCacheText();
    }

    private void UpdateCacheText()
    {
        var (files, bytes) = Tts.CacheUsage();
        CacheText = $"{files} 个缓存文件 · {bytes / 1024.0 / 1024.0:0.0} MB";
    }

    public void Save()
    {
        _store.Save();
        Raise(nameof(TodayCount));
        Raise(nameof(EnabledTaskCount));
        Raise(nameof(ProductCount));
        Raise(nameof(ScriptCount));
    }

    // ================= 节奏数据 =================
    public ObservableCollection<UpcomingRow> Upcoming { get; }
    public ObservableCollection<HourBar> HourBars { get; }

    private void RefreshUpcoming()
    {
        Upcoming.Clear();
        var now = DateTime.Now;
        foreach (var t in Tasks.Where(t => t.Enabled && t.NextRun is not null)
                     .OrderBy(t => t.NextRun).Take(4))
        {
            var next = t.NextRun!.Value;
            Upcoming.Add(new UpcomingRow
            {
                Time = next.Date == now.Date ? next.ToString("HH:mm") : "明天 " + next.ToString("HH:mm"),
                Name = string.IsNullOrWhiteSpace(t.Name) ? "未命名计划" : t.Name,
                Kind = t.KindText,
                Countdown = Humanize(next - now)
            });
        }
    }

    private void RebuildHourBars()
    {
        HourBars.Clear();
        var today = Engine.Logs.Where(l => l.At.Date == DateTime.Today).ToList();
        int max = Math.Max(1, today.GroupBy(l => l.At.Hour).Select(g => g.Count()).DefaultIfEmpty(1).Max());
        for (int h = 6; h <= 22; h++)
        {
            int count = today.Count(l => l.At.Hour == h);
            HourBars.Add(new HourBar { Label = h.ToString("00"), Count = count, Height = 8 + 52.0 * count / max });
        }
    }

    private void OnTick()
    {
        var now = DateTime.Now;
        ClockText = now.ToString("HH:mm:ss");
        Raise(nameof(ClockText));

        var next = Tasks.Where(t => t.Enabled && t.NextRun is not null).OrderBy(t => t.NextRun).FirstOrDefault();
        CountdownText = next?.NextRun is { } at ? Humanize(at - now) : "—";
        Raise(nameof(CountdownText));

        if (_tick % 15 == 0) RefreshUpcoming();
        if (_tick % 30 == 0) RebuildHourBars();
        _tick++;
    }

    private int _tick;

    private static string Humanize(TimeSpan span)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        if (span.TotalHours >= 1) return $"{(int)span.TotalHours} 小时 {span.Minutes} 分后";
        if (span.TotalMinutes >= 1) return $"{span.Minutes} 分 {span.Seconds} 秒后";
        return $"{span.Seconds} 秒后";
    }
}
