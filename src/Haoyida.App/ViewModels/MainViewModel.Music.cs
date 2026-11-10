using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using Haoyida.Core;

namespace Haoyida.ViewModels;

public sealed partial class MainViewModel
{
    public MusicService Music { get; private set; } = null!;
    public ObservableCollection<MusicTask> MusicTasks { get; } = new();
    public ObservableCollection<MusicTrack> MusicLibrary { get; } = new();
    public ObservableCollection<MusicTrack> MusicTaskTracks { get; } = new();

    private MusicTask? _selectedMusicTask;

    public MusicTask? SelectedMusicTask
    {
        get => _selectedMusicTask;
        set
        {
            if (!Set(ref _selectedMusicTask, value)) return;
            Raise(nameof(HasSelectedMusicTask));
            Raise(nameof(MusicWeekdayIndex));
            RefreshMusicTaskTracks();
        }
    }

    public bool HasSelectedMusicTask => SelectedMusicTask is not null;

    /// <summary>0=每天，1=工作日，2=周末。</summary>
    public int MusicWeekdayIndex
    {
        get
        {
            if (SelectedMusicTask is null) return 0;
            var days = SelectedMusicTask.Weekdays;
            if (days.Count == 7) return 0;
            if (days.SequenceEqual(new[] { 1, 2, 3, 4, 5 })) return 1;
            if (days.SequenceEqual(new[] { 6, 7 })) return 2;
            return 0;
        }
        set
        {
            if (SelectedMusicTask is null) return;
            SelectedMusicTask.Weekdays = value switch
            {
                1 => new List<int> { 1, 2, 3, 4, 5 },
                2 => new List<int> { 6, 7 },
                _ => new List<int> { 1, 2, 3, 4, 5, 6, 7 }
            };
            Raise(nameof(MusicWeekdayIndex));
            Save();
            Music.Restart();
        }
    }

    public string CurrentMusicTitle => Music.CurrentTrack?.Title ?? "未在播放";
    public string CurrentMusicTaskName => Music.ActiveTask?.Name ?? "当前时段没有生效的音乐任务";
    public string CurrentMusicMood => Music.CurrentTrack?.Mood ?? "";
    public bool MusicIsPlaying => Music.IsPlaying;

    public RelayCommand NewMusicTaskCommand { get; private set; } = null!;
    public RelayCommand DeleteMusicTaskCommand { get; private set; } = null!;
    public RelayCommand PlayMusicTaskCommand { get; private set; } = null!;
    public RelayCommand NextMusicCommand { get; private set; } = null!;
    public RelayCommand StopMusicCommand { get; private set; } = null!;
    public RelayCommand AddMusicFilesCommand { get; private set; } = null!;
    public RelayCommand RemoveTaskTrackCommand { get; private set; } = null!;
    public RelayCommand AddLibraryTrackCommand { get; private set; } = null!;
    public RelayCommand ShowComplianceCommand { get; private set; } = null!;

    private void InitMusic(MusicService music)
    {
        Music = music;

        foreach (var track in _store.Data.MusicTracks) MusicLibrary.Add(track);
        foreach (var task in _store.Data.MusicTasks) MusicTasks.Add(task);

        NewMusicTaskCommand = new RelayCommand(_ => NewMusicTask());
        DeleteMusicTaskCommand = new RelayCommand(_ => DeleteMusicTask(), _ => SelectedMusicTask is not null);
        PlayMusicTaskCommand = new RelayCommand(_ => { if (SelectedMusicTask is { } t) { Music.PlayTask(t); RaiseMusic(); } });
        NextMusicCommand = new RelayCommand(_ => { Music.Next(); RaiseMusic(); });
        StopMusicCommand = new RelayCommand(_ => { Music.Stop(); RaiseMusic(); });
        AddMusicFilesCommand = new RelayCommand(_ => AddMusicFiles());
        RemoveTaskTrackCommand = new RelayCommand(p => RemoveTaskTrack(p as MusicTrack), p => p is MusicTrack);
        AddLibraryTrackCommand = new RelayCommand(p => AddLibraryTrack(p as MusicTrack), p => p is MusicTrack);
        ShowComplianceCommand = new RelayCommand(_ =>
        {
            var window = new Haoyida.Views.ComplianceWindow { Owner = System.Windows.Application.Current.MainWindow };
            window.ShowDialog();
        });

        music.Changed += RaiseMusic;
        SelectedMusicTask = MusicTasks.FirstOrDefault();
    }

    private void RaiseMusic()
    {
        Raise(nameof(CurrentMusicTitle));
        Raise(nameof(CurrentMusicTaskName));
        Raise(nameof(CurrentMusicMood));
        Raise(nameof(MusicIsPlaying));
    }

    private void RefreshMusicTaskTracks()
    {
        MusicTaskTracks.Clear();
        if (SelectedMusicTask is null) return;
        foreach (var id in SelectedMusicTask.TrackIds)
        {
            var track = MusicLibrary.FirstOrDefault(t => t.Id == id);
            if (track is not null) MusicTaskTracks.Add(track);
        }
    }

    private void NewMusicTask()
    {
        var task = new MusicTask
        {
            Name = "音乐任务" + (MusicTasks.Count + 1),
            StartTime = "09:00",
            EndTime = "12:00",
            TrackIds = MusicLibrary.Where(t => t.IsBuiltIn).Take(1).Select(t => t.Id).ToList()
        };
        MusicTasks.Add(task);
        _store.Data.MusicTasks.Add(task);
        SelectedMusicTask = task;
        Save();
        Music.Restart();
    }

    private void DeleteMusicTask()
    {
        if (SelectedMusicTask is not { } task) return;
        MusicTasks.Remove(task);
        _store.Data.MusicTasks.Remove(task);
        SelectedMusicTask = MusicTasks.FirstOrDefault();
        Save();
        Music.Restart();
    }

    /// <summary>支持一次性添加多个本地音频文件到当前任务的播放列表。</summary>
    private void AddMusicFiles()
    {
        if (SelectedMusicTask is null)
        {
            MessageBox.Show("请先在左侧选择一个音乐任务，再添加音频。", "耗易达");
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "添加背景音乐（可多选）",
            Filter = "音频文件|*.mp3;*.wav;*.wma;*.m4a;*.aac;*.flac|所有文件|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog() != true) return;

        int added = 0;
        foreach (var file in dialog.FileNames)
        {
            if (!File.Exists(file)) continue;

            // 同一个文件不重复入库
            var track = MusicLibrary.FirstOrDefault(t => !t.IsBuiltIn &&
                                                          string.Equals(t.FilePath, file, StringComparison.OrdinalIgnoreCase));
            if (track is null)
            {
                track = new MusicTrack
                {
                    Title = Path.GetFileNameWithoutExtension(file),
                    FilePath = file,
                    IsBuiltIn = false,
                    Mood = "本地音频"
                };
                MusicLibrary.Add(track);
                _store.Data.MusicTracks.Add(track);
            }

            if (!SelectedMusicTask.TrackIds.Contains(track.Id))
            {
                SelectedMusicTask.TrackIds.Add(track.Id);
                added++;
            }
        }

        RefreshMusicTaskTracks();
        SelectedMusicTask.RefreshTrackCount();
        Save();
        Music.Restart();
        Raise(nameof(SelectedMusicTask));
    }

    private void RemoveTaskTrack(MusicTrack? track)
    {
        if (track is null || SelectedMusicTask is null) return;
        SelectedMusicTask.TrackIds.Remove(track.Id);
        RefreshMusicTaskTracks();
        SelectedMusicTask.RefreshTrackCount();
        Save();
        Music.Restart();
    }

    private void AddLibraryTrack(MusicTrack? track)
    {
        if (track is null || SelectedMusicTask is null) return;
        if (SelectedMusicTask.TrackIds.Contains(track.Id)) return;
        SelectedMusicTask.TrackIds.Add(track.Id);
        RefreshMusicTaskTracks();
        SelectedMusicTask.RefreshTrackCount();
        Save();
        Music.Restart();
    }
}
