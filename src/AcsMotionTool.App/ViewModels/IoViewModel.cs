using System.Collections.ObjectModel;
using AcsMotionTool.App.Infrastructure;
using AcsMotionTool.App.Services;

namespace AcsMotionTool.App.ViewModels;

/// <summary>一个 IO 端口(16 位一组，用于界面按端口分组显示)</summary>
public sealed class IoPortViewModel : ViewModelBase
{
    public IoPortViewModel(int port, bool isOutput)
    {
        Port = port;
        IsOutput = isOutput;
    }

    public int Port { get; }
    public bool IsOutput { get; }

    public ObservableCollection<IoBitViewModel> Bits { get; } = new();

    public string Title => $"端口 {Port}";

    /// <summary>当前端口值(按位合成)</summary>
    public string HexText
    {
        get
        {
            var value = 0;
            foreach (var bit in Bits)
            {
                if (bit.IsOn) value |= 1 << bit.Bit;
            }

            var digits = Math.Max(2, (Bits.Count + 3) / 4);
            return "0x" + value.ToString("X" + digits);
        }
    }

    public void RefreshHex() => OnPropertyChanged(nameof(HexText));
}

/// <summary>
/// IO 页 ViewModel:按端口分组的数字输入监视、数字输出开关、整端口写入、模拟量读写。
/// </summary>
public sealed class IoViewModel : ViewModelBase
{
    private readonly IAcsService _acs;
    private readonly IAppSettingsService _settings;
    private readonly ILogService _log;

    public IoViewModel(IAcsService acs, IAppSettingsService settings, ILogService log)
    {
        _acs = acs;
        _settings = settings;
        _log = log;

        WritePortCommand = CreateAsyncCommand(WritePortAsync, () => IsConnected);
        WriteAnalogCommand = CreateAsyncCommand(WriteAnalogAsync, () => IsConnected);
        ReadAnalogCommand = CreateAsyncCommand(ReadAnalogAsync, () => IsConnected);

        ApplyLayout(_settings.Current.IOPortCount, _settings.Current.IOBitsPerPort);
        _acs.IoUpdated += OnIoUpdated;
        _acs.ConnectionStateChanged += OnConnectionStateChanged;
    }

    #region 集合与属性

    public ObservableCollection<IoPortViewModel> InputPorts { get; } = new();
    public ObservableCollection<IoPortViewModel> OutputPorts { get; } = new();

    private int _portCount = 2;
    private int _bitsPerPort = 16;
    private int _portNumber;
    private string _portValueText = "0";
    private string _analogValueText = "0";
    private string _analogResultText = string.Empty;

    public int PortCount { get => _portCount; set => SetField(ref _portCount, value); }
    public int BitsPerPort { get => _bitsPerPort; set => SetField(ref _bitsPerPort, value); }

    public string LayoutText => $"{PortCount} 端口 x {BitsPerPort} 位";

    /// <summary>整端口写入用的端口号</summary>
    public int PortNumber { get => _portNumber; set => SetField(ref _portNumber, value); }

    /// <summary>整端口写入值(支持 0x 十六进制)</summary>
    public string PortValueText { get => _portValueText; set => SetField(ref _portValueText, value); }

    /// <summary>模拟量输出值</summary>
    public string AnalogValueText { get => _analogValueText; set => SetField(ref _analogValueText, value); }

    /// <summary>模拟量读取结果显示</summary>
    public string AnalogResultText { get => _analogResultText; set => SetField(ref _analogResultText, value); }

    public bool IsConnected => _acs.IsConnected;

    #endregion

    #region 命令

    public AsyncRelayCommand WritePortCommand { get; }
    public AsyncRelayCommand WriteAnalogCommand { get; }
    public AsyncRelayCommand ReadAnalogCommand { get; }

    public void ApplyLayout(int ports, int bitsPerPort)
    {
        PortCount = Math.Max(1, ports);
        BitsPerPort = Math.Clamp(bitsPerPort, 1, 32);

        InputPorts.Clear();
        OutputPorts.Clear();

        for (var port = 0; port < PortCount; port++)
        {
            var inputPort = new IoPortViewModel(port, isOutput: false);
            var outputPort = new IoPortViewModel(port, isOutput: true);

            for (var bit = 0; bit < BitsPerPort; bit++)
            {
                inputPort.Bits.Add(new IoBitViewModel(this, port, bit, isOutput: false));
                outputPort.Bits.Add(new IoBitViewModel(this, port, bit, isOutput: true));
            }

            InputPorts.Add(inputPort);
            OutputPorts.Add(outputPort);
        }

        OnPropertyChanged(nameof(LayoutText));
        _acs.SetIoLayout(Enumerable.Range(0, PortCount).ToList(), BitsPerPort);
        _log.Info($"IO 列表已重建: {PortCount} 端口 x {BitsPerPort} 位", "IO");
    }

    /// <summary>输出位被点击时由 IoBitViewModel 调用(写失败则回滚显示)</summary>
    internal async Task ToggleOutputAsync(IoBitViewModel bit, bool value)
    {
        var ok = await _acs.SetOutputBitAsync(bit.Port, bit.Bit, value).ConfigureAwait(true);
        if (!ok) bit.ApplyFromDevice(!value);
    }

    private async Task WritePortAsync()
    {
        if (!TryParseInt(PortValueText, out var value))
        {
            _log.Warn($"输出值非法(支持十进制或 0x 十六进制): {PortValueText}", "IO");
            return;
        }
        await _acs.SetOutputPortAsync(PortNumber, value).ConfigureAwait(true);
    }

    private async Task WriteAnalogAsync()
    {
        if (!double.TryParse(AnalogValueText, out var value))
        {
            _log.Warn($"模拟量数值非法: {AnalogValueText}", "IO");
            return;
        }
        await _acs.WriteAnalogOutputAsync(PortNumber, value).ConfigureAwait(true);
    }

    private async Task ReadAnalogAsync()
    {
        var (raw, scaled) = await _acs.ReadAnalogInputAsync(PortNumber).ConfigureAwait(true);
        AnalogResultText = $"端口 {PortNumber}: 原始={raw}, 换算={scaled:F6}";
        _log.Info($"模拟输入 {AnalogResultText}", "IO");
    }

    #endregion

    #region 状态刷新

    private void OnIoUpdated(object? sender, IReadOnlyList<IoPortState> states)
    {
        foreach (var state in states)
        {
            var inputPort = InputPorts.FirstOrDefault(p => p.Port == state.Port);
            if (inputPort != null)
            {
                foreach (var bit in inputPort.Bits)
                {
                    bit.ApplyFromDevice(((state.Input >> bit.Bit) & 0x01) != 0);
                }
                inputPort.RefreshHex();
            }

            var outputPort = OutputPorts.FirstOrDefault(p => p.Port == state.Port);
            if (outputPort != null)
            {
                foreach (var bit in outputPort.Bits)
                {
                    bit.ApplyFromDevice(((state.Output >> bit.Bit) & 0x01) != 0);
                }
                outputPort.RefreshHex();
            }
        }
    }

    private void OnConnectionStateChanged(object? sender, bool connected)
    {
        OnPropertyChanged(nameof(IsConnected));
        RaiseAllCanExecuteChanged();
    }

    private static bool TryParseInt(string? text, out int value)
    {
        value = 0;
        text = text?.Trim() ?? string.Empty;
        if (text.Length == 0) return false;
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(text[2..], System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out value);
        }
        return int.TryParse(text, out value);
    }

    #endregion
}
