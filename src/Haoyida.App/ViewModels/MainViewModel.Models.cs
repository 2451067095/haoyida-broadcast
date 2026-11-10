using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using Haoyida.Core;
using Haoyida.Tts;

namespace Haoyida.ViewModels;

public sealed partial class MainViewModel
{
    public ObservableCollection<CustomTtsModel> CustomModels { get; } = new();

    public RelayCommand InstallModelCommand { get; private set; } = null!;
    public RelayCommand DeleteModelCommand { get; private set; } = null!;
    public RelayCommand EditModelCommand { get; private set; } = null!;
    public RelayCommand StartModelServiceCommand { get; private set; } = null!;
    public RelayCommand StopModelServiceCommand { get; private set; } = null!;

    private void InitCustomModels()
    {
        foreach (var m in _store.Data.CustomModels) CustomModels.Add(m);
        Tts.ReloadCustomEngines(CustomModels);

        InstallModelCommand = new RelayCommand(_ => OpenModelEditor(null));
        EditModelCommand = new RelayCommand(p => OpenModelEditor(p as CustomTtsModel), p => p is CustomTtsModel);
        DeleteModelCommand = new RelayCommand(p => DeleteModel(p as CustomTtsModel), p => p is CustomTtsModel);
        StartModelServiceCommand = new RelayCommand(async p =>
        {
            if (p is not CustomTtsModel m) return;
            var (ok, message) = await LocalServiceManager.StartAsync(m);
            MessageBox.Show(ok ? "服务已就绪，可以直接试听。" : "启动失败：" + message, "耗易达");
            Tts.ReloadCustomEngines(CustomModels);
            RebuildEngineCards();
        }, p => p is CustomTtsModel);
        StopModelServiceCommand = new RelayCommand(p =>
        {
            if (p is not CustomTtsModel m) return;
            LocalServiceManager.Stop(m.Id);
            Tts.ReloadCustomEngines(CustomModels);
            RebuildEngineCards();
        }, p => p is CustomTtsModel);
    }

    private void OpenModelEditor(CustomTtsModel? model)
    {
        var dialog = new Haoyida.Views.ModelInstallWindow(model, _store.DataDirectory)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };

        if (dialog.ShowDialog() != true) return;

        var result = dialog.Result;
        if (model is null)
        {
            CustomModels.Add(result);
            _store.Data.CustomModels.Add(result);
        }
        else
        {
            // 对话框里编辑的是副本，回写字段
            model.Name = result.Name;
            model.Mode = result.Mode;
            model.Command = result.Command;
            model.BodyTemplate = result.BodyTemplate;
            model.VoiceList = result.VoiceList;
            model.License = result.License;
            model.WorkDir = result.WorkDir;
            model.Notes = result.Notes;
        }

        Save();
        Tts.ReloadCustomEngines(CustomModels);
        RebuildEngineCards();
        Raise(nameof(CustomModels));
        Raise(nameof(HasCustomModels));
    }

    private void DeleteModel(CustomTtsModel? model)
    {
        if (model is null) return;
        if (MessageBox.Show($"确定移除「{model.Name}」吗？不会删除磁盘上的模型文件。", "耗易达",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;

        CustomModels.Remove(model);
        _store.Data.CustomModels.Remove(model);
        Save();
        Tts.ReloadCustomEngines(CustomModels);
        RebuildEngineCards();
        Raise(nameof(HasCustomModels));
    }

    public bool HasCustomModels => CustomModels.Count > 0;

    private void RebuildEngineCards()
    {
        BuildEngineCards();
        UpdateEngineStatus();
        RefreshEngineChoices();
    }
}
