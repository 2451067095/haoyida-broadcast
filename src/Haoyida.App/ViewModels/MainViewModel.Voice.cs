using System.Collections.ObjectModel;
using Haoyida.Tts;

namespace Haoyida.ViewModels;

public sealed record EngineChoice(string Id, string Name);

public sealed partial class MainViewModel
{
    public ObservableCollection<EngineChoice> TaskEngineOptions { get; } = new();
    public ObservableCollection<VoiceOption> TaskVoices { get; } = new();

    private string _taskEngineId = "";

    public string TaskEngineId
    {
        get => _taskEngineId;
        set
        {
            if (!Set(ref _taskEngineId, value)) return;
            if (SelectedTask is not null) SelectedTask.EngineId = value;
            RefreshTaskVoices();
            Raise(nameof(TaskVoiceId));
            Save();
        }
    }

    /// <summary>任务音色；空串表示跟随全局默认。</summary>
    public string TaskVoiceId
    {
        get => SelectedTask?.VoiceId ?? "";
        set
        {
            if (SelectedTask is null) return;
            SelectedTask.VoiceId = value ?? "";
            Raise(nameof(TaskVoiceId));
            Save();
        }
    }

    public string DefaultVoiceText
    {
        get
        {
            var engine = Tts.Engines.FirstOrDefault(e => e.Id == Profile.DefaultEngineId);
            var voice = engine?.Voices.FirstOrDefault(v => v.Id == Profile.DefaultVoiceId);
            return engine is null
                ? "未设置"
                : $"{engine.Name} · {voice?.Name ?? Profile.DefaultVoiceId}";
        }
    }

    public RelayCommand SetDefaultVoiceCommand { get; private set; } = null!;
    public RelayCommand PreviewVoiceItemCommand { get; private set; } = null!;

    private void InitVoicePickers()
    {
        SetDefaultVoiceCommand = new RelayCommand(_ =>
        {
            if (SelectedVoice is null) return;
            Profile.DefaultEngineId = SelectedVoice.EngineId;
            Profile.DefaultVoiceId = SelectedVoice.Id;
            Save();
            Raise(nameof(DefaultVoiceText));
            Raise(nameof(Profile));
        });

        PreviewVoiceItemCommand = new RelayCommand(p =>
        {
            if (p is not VoiceOption v) return;
            SelectedVoice = v;
            _ = PreviewAsync(string.IsNullOrWhiteSpace(PreviewText)
                ? "欢迎光临，本店今日有特惠。"
                : PreviewText);
        });

        RefreshEngineChoices();
    }

    /// <summary>引擎列表变化（装了第三方模型）后刷新下拉。</summary>
    public void RefreshEngineChoices()
    {
        var previous = _taskEngineId;
        TaskEngineOptions.Clear();
        TaskEngineOptions.Add(new EngineChoice("", "跟随默认音色"));
        foreach (var engine in Tts.Engines.Where(e => e.IsAvailable(out _)))
            TaskEngineOptions.Add(new EngineChoice(engine.Id, engine.Name));

        _taskEngineId = previous;
        Raise(nameof(TaskEngineId));
        Raise(nameof(DefaultVoiceText));
        RefreshTaskVoices();
    }

    private void RefreshTaskVoices()
    {
        TaskVoices.Clear();
        var engineId = string.IsNullOrEmpty(_taskEngineId) ? Profile.DefaultEngineId : _taskEngineId;
        var engine = Tts.Engines.FirstOrDefault(e => e.Id == engineId)
                     ?? Tts.Engines.FirstOrDefault(e => e.IsAvailable(out _));
        if (engine is null) return;

        var model = CustomModels.FirstOrDefault(m => "ext:" + m.Id == engine.Id);
        if (model is null && engine.IsAvailable(out _))
            foreach (var v in engine.Voices) TaskVoices.Add(v);
    }

    /// <summary>切换计划时同步语音下拉。</summary>
    public void SyncTaskVoice()
    {
        _taskEngineId = SelectedTask?.EngineId ?? "";
        Raise(nameof(TaskEngineId));
        Raise(nameof(TaskVoiceId));
        RefreshTaskVoices();
    }
}
