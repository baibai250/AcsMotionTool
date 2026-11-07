using AcsMotionTool.App.Infrastructure;
using AcsMotionTool.App.Services;
using AcsMotionTool.Core;

namespace AcsMotionTool.App.ViewModels;

/// <summary>轴列表中的一行(纯展示 VM，由 AxisViewModel 用服务推送的状态刷新)</summary>
public sealed class AxisRowViewModel : ViewModelBase
{
    private AcsAxisStatus _status;
    private string _name;

    public AxisRowViewModel(int index, string name)
    {
        Index = index;
        _name = name;
        _status = AcsAxisStatus.Invalid(index, name, null);
    }

    public int Index { get; }

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public bool HasAlarm => _status.HasFault;
    public bool IsMoving => _status.IsMoving;
    public bool IsEnabled => _status.IsEnabled;
    public int MotionCode => _status.MotionError;
    public double Position => _status.EncoderPosition;

    public string PositionText => _status.IsValid ? _status.EncoderPosition.ToString("F4") : "--";
    public string CommandPositionText => _status.IsValid ? _status.CommandPosition.ToString("F4") : "--";
    public string TargetPositionText => _status.IsValid ? _status.TargetPosition.ToString("F4") : "--";
    public string VelocityText => _status.IsValid ? _status.Velocity.ToString("F4") : "--";
    public string EnabledText => _status.IsEnabled ? "使能" : "未使能";
    public string InPositionText => !_status.IsValid || !_status.IsEnabled
        ? "--"
        : (_status.IsMoving ? "运动" : (_status.IsInPosition ? "到位" : "未到位"));
    public string LimitText => (_status.NegativeLimit ? "-限 " : "") + (_status.PositiveLimit ? "+限" : "");

    /// <summary>只有 5009 及以上的运动码、或驱动器报警才算报警</summary>
    public string AlarmText => _status.HasFault
        ? (_status.DriveAlarm ? "驱动器报警" : $"故障 {_status.MotionError}")
        : string.Empty;

    /// <summary>运动终止码:5000~5008 是正常终止(HALT/KILL/下使能)，不是报警</summary>
    public string MotionCodeText => _status.MotionError == 0
        ? string.Empty
        : _status.IsMotionFault ? $"{_status.MotionError} 故障" : $"{_status.MotionError} 正常";

    public string MotionCodeDetail => AcsMotionErrors.DescribeForDisplay(_status.MotionError);

    public void Apply(AcsAxisStatus status)
    {
        // 数值没变就不触发 UI 刷新:长时间运行时(轴静止)可以省掉绝大部分绑定计算
        if (_status.HasSameValues(status)) return;

        _status = status;
        OnAllPropertiesChanged();
    }
}

/// <summary>IO 位(输入只读/输出可点)VM</summary>
public sealed class IoBitViewModel : ViewModelBase
{
    private readonly IoViewModel _owner;
    private bool _isOn;
    private bool _suppressWrite;

    public IoBitViewModel(IoViewModel owner, int port, int bit, bool isOutput)
    {
        _owner = owner;
        Port = port;
        Bit = bit;
        IsOutput = isOutput;
    }

    public int Port { get; }
    public int Bit { get; }
    public bool IsOutput { get; }
    public bool CanToggle => IsOutput;
    public string Label => $"{Port}.{Bit}";

    /// <summary>输出位:勾选即写硬件(轮询回读时不会再次触发写入)</summary>
    public bool IsOn
    {
        get => _isOn;
        set
        {
            if (!SetField(ref _isOn, value)) return;
            if (IsOutput && !_suppressWrite) _ = _owner.ToggleOutputAsync(this, value);
        }
    }

    /// <summary>由轮询更新显示值(不触发写硬件)</summary>
    public void ApplyFromDevice(bool value)
    {
        _suppressWrite = true;
        try
        {
            IsOn = value;
        }
        finally
        {
            _suppressWrite = false;
        }
    }
}

/// <summary>被监视变量一行</summary>
public sealed class VariableRowViewModel : ViewModelBase
{
    private string _valueText = string.Empty;
    private bool _hasError;

    public VariableRowViewModel(string name, int buffer = -1)
    {
        Name = name;
        Buffer = buffer;
    }

    public string Name { get; set; }
    public int Buffer { get; set; }

    public string ValueText
    {
        get => _valueText;
        set => SetField(ref _valueText, value);
    }

    public bool HasError
    {
        get => _hasError;
        set => SetField(ref _hasError, value);
    }

    public void Apply(VariableValue value)
    {
        ValueText = value.Text;
        HasError = value.HasError;
    }
}
