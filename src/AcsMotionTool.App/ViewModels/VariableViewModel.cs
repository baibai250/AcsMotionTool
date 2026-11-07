using System.Collections.ObjectModel;
using AcsMotionTool.App.Infrastructure;
using AcsMotionTool.App.Services;
using AcsMotionTool.Core;

namespace AcsMotionTool.App.ViewModels;

/// <summary>变量页 ViewModel:变量读写、声明、监视列表(周期刷新由服务推送)</summary>
public sealed class VariableViewModel : ViewModelBase
{
    private readonly IAcsService _acs;
    private readonly IAppSettingsService _settings;
    private readonly ILogService _log;

    public VariableViewModel(IAcsService acs, IAppSettingsService settings, ILogService log)
    {
        _acs = acs;
        _settings = settings;
        _log = log;

        foreach (var watch in settings.Current.WatchedVariables)
        {
            WatchedVariables.Add(new VariableRowViewModel(watch.Name, watch.Buffer));
        }

        ReadCommand = CreateAsyncCommand(ReadAsync, () => HasVariableName);
        WriteCommand = CreateAsyncCommand(WriteAsync, () => HasVariableName);
        DeclareRealCommand = CreateAsyncCommand(() => DeclareAsync(false), () => HasVariableName);
        DeclareIntegerCommand = CreateAsyncCommand(() => DeclareAsync(true), () => HasVariableName);
        AddWatchCommand = CreateCommand(AddWatch, () => HasVariableName);
        RemoveWatchCommand = CreateCommand(RemoveWatch);
        RefreshWatchesCommand = CreateAsyncCommand(RefreshWatchesAsync);

        _acs.VariablesUpdated += OnVariablesUpdated;
        PublishWatches();
    }

    #region 集合与属性

    public ObservableCollection<VariableRowViewModel> WatchedVariables { get; } = new();

    private string _variableName = "?FPOS(0)";
    private string _writeValue = "0";

    public string VariableName
    {
        get => _variableName;
        set
        {
            if (SetField(ref _variableName, value)) OnPropertyChanged(nameof(HasVariableName));
        }
    }

    public string WriteValue { get => _writeValue; set => SetField(ref _writeValue, value); }

    public bool HasVariableName => !string.IsNullOrWhiteSpace(VariableName);

    #endregion

    #region 命令

    public AsyncRelayCommand ReadCommand { get; }
    public AsyncRelayCommand WriteCommand { get; }
    public AsyncRelayCommand DeclareRealCommand { get; }
    public AsyncRelayCommand DeclareIntegerCommand { get; }
    public RelayCommand AddWatchCommand { get; }
    public RelayCommand RemoveWatchCommand { get; }
    public AsyncRelayCommand RefreshWatchesCommand { get; }

    #endregion

    private async Task ReadAsync()
    {
        var name = VariableName.Trim();
        var text = await _acs.ReadVariableTextAsync(name).ConfigureAwait(true);
        _log.Info($"变量 {name} = {text}", "变量");
    }

    private async Task WriteAsync()
    {
        var name = VariableName.Trim();
        await _acs.WriteVariableFromTextAsync(name, WriteValue).ConfigureAwait(true);
    }

    private async Task DeclareAsync(bool integer)
    {
        var name = VariableName.Trim();
        await _acs.DeclareVariableAsync(name, integer).ConfigureAwait(true);
    }

    private void AddWatch()
    {
        var name = VariableName.Trim();
        if (WatchedVariables.Any(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            _log.Warn($"变量 {name} 已在监视列表中", "变量");
            return;
        }

        WatchedVariables.Add(new VariableRowViewModel(name));
        PersistWatches();
        _log.Info($"已添加监视变量 {name}", "变量");
    }

    private void RemoveWatch(object? parameter)
    {
        if (parameter is not VariableRowViewModel row) return;
        WatchedVariables.Remove(row);
        PersistWatches();
        _log.Info($"已移除监视变量 {row.Name}", "变量");
    }

    private async Task RefreshWatchesAsync()
    {
        foreach (var row in WatchedVariables)
        {
            var text = await _acs.ReadVariableTextAsync(row.Name, row.Buffer).ConfigureAwait(true);
            row.ValueText = text;
            row.HasError = string.IsNullOrEmpty(text);
        }
    }

    private void OnVariablesUpdated(object? sender, IReadOnlyList<VariableValue> values)
    {
        foreach (var value in values)
        {
            var row = WatchedVariables.FirstOrDefault(v =>
                string.Equals(v.Name, value.Name, StringComparison.OrdinalIgnoreCase) && v.Buffer == value.Buffer);
            row?.Apply(value);
        }
    }

    private void PublishWatches()
        => _acs.SetVariableWatches(WatchedVariables.Select(v => new VariableWatch(v.Name, v.Buffer)).ToList());

    private void PersistWatches()
    {
        _settings.Current.WatchedVariables = WatchedVariables
            .Select(v => new AcsVariableWatch { Name = v.Name, Buffer = v.Buffer })
            .ToList();
        _settings.Save();
        PublishWatches();
    }
}
