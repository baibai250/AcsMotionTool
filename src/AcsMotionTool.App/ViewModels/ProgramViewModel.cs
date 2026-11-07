using AcsMotionTool.App.Infrastructure;
using AcsMotionTool.App.Services;

namespace AcsMotionTool.App.ViewModels;

/// <summary>程序缓冲区页 ViewModel:ACSPL+ 程序的上传/下载/编译/运行/停止</summary>
public sealed class ProgramViewModel : ViewModelBase
{
    private readonly IAcsService _acs;
    private readonly ILogService _log;

    public ProgramViewModel(IAcsService acs, ILogService log)
    {
        _acs = acs;
        _log = log;

        UploadCommand = CreateAsyncCommand(UploadAsync, () => IsConnected);
        DownloadCommand = CreateAsyncCommand(DownloadAsync, () => IsConnected);
        CompileCommand = CreateAsyncCommand(CompileAsync, () => IsConnected);
        RunCommand = CreateAsyncCommand(RunAsync, () => IsConnected);
        StopCommand = CreateAsyncCommand(StopAsync, () => IsConnected);
        InsertTemplateCommand = CreateCommand(InsertTemplate);

        _acs.ConnectionStateChanged += OnConnectionStateChanged;
    }

    private int _bufferIndex;
    private string _programText = string.Empty;

    public int BufferIndex { get => _bufferIndex; set => SetField(ref _bufferIndex, value); }

    public string ProgramText { get => _programText; set => SetField(ref _programText, value); }

    public bool IsConnected => _acs.IsConnected;

    public AsyncRelayCommand UploadCommand { get; }
    public AsyncRelayCommand DownloadCommand { get; }
    public AsyncRelayCommand CompileCommand { get; }
    public AsyncRelayCommand RunCommand { get; }
    public AsyncRelayCommand StopCommand { get; }
    public RelayCommand InsertTemplateCommand { get; }

    private async Task UploadAsync()
    {
        var text = await _acs.UploadProgramAsync(BufferIndex).ConfigureAwait(true);
        ProgramText = text;
        _log.Info($"已上传缓冲区 {BufferIndex}({text.Length} 字符)", "程序");
    }

    private async Task DownloadAsync()
    {
        await _acs.DownloadProgramAsync(BufferIndex, ProgramText).ConfigureAwait(true);
    }

    private async Task CompileAsync()
    {
        await _acs.CompileProgramAsync(BufferIndex).ConfigureAwait(true);
    }

    private async Task RunAsync()
    {
        await _acs.RunProgramAsync(BufferIndex).ConfigureAwait(true);
    }

    private async Task StopAsync()
    {
        await _acs.StopProgramAsync(BufferIndex).ConfigureAwait(true);
    }

    /// <summary>插入一段回零程序模板(ASCII + ! 注释，控制器可直接编译)</summary>
    private void InsertTemplate()
    {
        // 说明:FPOS 是只读变量，置零必须用 SET RPOS；控制器注释符是 !
        ProgramText = "!Single axis homing template\r\n" +
                      "HOME 0, 17, 100, 100000\r\n" +
                      "STOP\r\n";
        _log.Info("已插入回零程序模板(记得把轴号/回零方式改成现场实际值)", "程序");
    }

    private void OnConnectionStateChanged(object? sender, bool connected)
    {
        OnPropertyChanged(nameof(IsConnected));
        RaiseAllCanExecuteChanged();
    }
}
